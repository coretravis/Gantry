using Gantry.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Gantry.Infrastructure.Runtimes;

public class DotNetRuntimeProvider : IRuntimeProvider, IOsVersionAwareProvider
{
    private readonly ILogger<DotNetRuntimeProvider> _logger;

    public DotNetRuntimeProvider(ILogger<DotNetRuntimeProvider> logger) => _logger = logger;

    public string Ecosystem => "dotnet";
    public IReadOnlyList<string> SupportedVersions => ["6.0", "7.0", "8.0", "9.0", "10.0"];

    /// <summary>
    /// Supported .NET versions per Ubuntu LTS release. Versions absent from a row are either
    /// EOL with no backport PPA entry or simply not tested/supported on that OS version.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> CompatibilityMatrix { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["22.04"] = ["6.0", "7.0", "8.0", "9.0", "10.0"],
            ["24.04"] = ["8.0", "9.0", "10.0"]
        };

    public bool IsVersionSupportedOnOs(string runtimeVersion, string osVersion)
    {
        // Strip "ubuntu-" prefix if present (config may store "ubuntu-22.04" or "22.04")
        var normalized = osVersion.Replace("ubuntu-", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (!CompatibilityMatrix.TryGetValue(normalized, out var supported))
            return true; // Unknown OS version - allow and let the install attempt surface the real error
        return supported.Contains(runtimeVersion, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> SupportedVersionsForOs(string osVersion)
    {
        var normalized = osVersion.Replace("ubuntu-", string.Empty, StringComparison.OrdinalIgnoreCase);
        return CompatibilityMatrix.TryGetValue(normalized, out var supported)
            ? supported
            : SupportedVersions;
    }

    public async Task InstallAsync(ISshService ssh, string version, bool isDryRun, CancellationToken ct = default)
    {
        var ubuntuVersion = await GetUbuntuVersionAsync(ssh, isDryRun, ct);
        _logger.LogInformation("Installing aspnetcore-runtime-{Version} on Ubuntu {Ubuntu}", version, ubuntuVersion);

        if (isDryRun)
        {
            _logger.LogDebug("[dry-run] Would install aspnetcore-runtime-{Version}", version);
            return;
        }

        var strategy = GetInstallStrategy(ubuntuVersion, version);
        _logger.LogDebug("Install strategy for .NET {Version} on Ubuntu {Ubuntu}: {Strategy}", version, ubuntuVersion, strategy);

        switch (strategy)
        {
            case InstallStrategy.UbuntuNative:
                await InstallFromUbuntuNativeAsync(ssh, version, ct);
                break;
            case InstallStrategy.UbuntuBackports:
                await InstallFromUbuntuBackportsAsync(ssh, version, ct);
                break;
            case InstallStrategy.MicrosoftFeed:
                await InstallFromMicrosoftFeedAsync(ssh, version, ubuntuVersion, ct);
                break;
        }
    }

    public async Task<bool> IsInstalledAsync(ISshService ssh, string version, CancellationToken ct = default)
    {
        var result = await ssh.ExecuteAsync("dotnet --list-runtimes 2>/dev/null", ct: ct);
        if (!result.Success) return false;

        return result.Stdout.Split('\n')
            .Any(line => line.Contains("Microsoft.AspNetCore.App") && line.Contains(version));
    }

    public async Task<string> GetExecutablePathAsync(ISshService ssh, CancellationToken ct = default)
    {
        var result = await ssh.ExecuteAsync("which dotnet", ct: ct);
        return result.Success ? result.Stdout.Trim() : "/usr/bin/dotnet";
    }

    public async Task RollbackAsync(ISshService ssh, string version, CancellationToken ct = default)
    {
        _logger.LogInformation("Removing aspnetcore-runtime-{Version}", version);
        await ssh.ExecuteAsync($"apt-get remove -y aspnetcore-runtime-{version} 2>/dev/null || true", ct: ct);
    }

    // Install strategy matrix:
    //
    //                 Ubuntu 22.04   Ubuntu 24.04   Ubuntu < 22.04
    //  .NET 6/7       Native         Backports*     MicrosoftFeed
    //  .NET 8         Native         Native         MicrosoftFeed
    //  .NET 9/10+     Backports PPA  Backports PPA  MicrosoftFeed
    //
    // * Ubuntu 24.04 ships only .NET 8 natively. .NET 6 and 7 are EOL and not in the
    //   backports PPA; provisioning with those versions on Ubuntu 24.04 will likely fail.
    //   Ubuntu 22.04 ships .NET 6, 7, and 8 natively.
    // * The dotnet/backports PPA (ppa:dotnet/backports) carries .NET 9+ for both LTS releases.
    private static InstallStrategy GetInstallStrategy(string ubuntuVersion, string dotNetVersion)
    {
        var ubuntuMajor = ParseMajorVersion(ubuntuVersion);
        var dotNetMajor = ParseMajorVersion(dotNetVersion);

        if (ubuntuMajor >= 24)
            return dotNetMajor == 8 ? InstallStrategy.UbuntuNative : InstallStrategy.UbuntuBackports;

        if (ubuntuMajor == 22)
            return dotNetMajor <= 8 ? InstallStrategy.UbuntuNative : InstallStrategy.UbuntuBackports;

        return InstallStrategy.MicrosoftFeed;
    }

    private async Task InstallFromUbuntuNativeAsync(ISshService ssh, string version, CancellationToken ct)
    {
        // Remove Microsoft feed if present to avoid package conflicts
        await ExecuteAsync(ssh,
            "rm -f /etc/apt/sources.list.d/microsoft-prod.list /tmp/packages-microsoft-prod.deb 2>/dev/null; apt-get update -qq",
            ct: ct);
        await ExecuteAsync(ssh,
            $"DEBIAN_FRONTEND=noninteractive apt-get install -y aspnetcore-runtime-{version}",
            timeout: TimeSpan.FromMinutes(5), ct: ct);
    }

    private async Task InstallFromUbuntuBackportsAsync(ISshService ssh, string version, CancellationToken ct)
    {
        // .NET 9+ is not in Ubuntu's default repos; the dotnet/backports PPA carries it.
        // Remove any Microsoft feed first to avoid conflicts.
        await ExecuteAsync(ssh,
            "rm -f /etc/apt/sources.list.d/microsoft-prod.list /tmp/packages-microsoft-prod.deb 2>/dev/null || true",
            ct: ct);
        await ExecuteAsync(ssh,
            "DEBIAN_FRONTEND=noninteractive apt-get install -y software-properties-common 2>/dev/null || true",
            ct: ct);
        await ExecuteAsync(ssh, "add-apt-repository -y ppa:dotnet/backports", timeout: TimeSpan.FromMinutes(3), ct: ct);
        await ExecuteAsync(ssh, "apt-get update -qq", ct: ct);
        await ExecuteAsync(ssh,
            $"DEBIAN_FRONTEND=noninteractive apt-get install -y aspnetcore-runtime-{version}",
            timeout: TimeSpan.FromMinutes(5), ct: ct);
    }

    private async Task InstallFromMicrosoftFeedAsync(ISshService ssh, string version, string ubuntuVersion, CancellationToken ct)
    {
        // Ubuntu < 22.04: .NET is not in Ubuntu's repos at all; use packages.microsoft.com.
        var pkgUrl = $"https://packages.microsoft.com/config/ubuntu/{ubuntuVersion}/packages-microsoft-prod.deb";
        await ExecuteAsync(ssh, $"wget -q {pkgUrl} -O /tmp/packages-microsoft-prod.deb", ct: ct);
        await ExecuteAsync(ssh, "dpkg -i /tmp/packages-microsoft-prod.deb", ct: ct);
        await ExecuteAsync(ssh, "apt-get update -qq", ct: ct);
        await ExecuteAsync(ssh,
            $"DEBIAN_FRONTEND=noninteractive apt-get install -y aspnetcore-runtime-{version}",
            timeout: TimeSpan.FromMinutes(5), ct: ct);
        await ExecuteAsync(ssh, "rm -f /tmp/packages-microsoft-prod.deb", ct: ct);
    }

    private static async Task<string> GetUbuntuVersionAsync(ISshService ssh, bool isDryRun, CancellationToken ct)
    {
        if (isDryRun) return "24.04";
        var result = await ssh.ExecuteAsync("lsb_release -r -s", ct: ct);
        return result.Success ? result.Stdout.Trim() : "24.04";
    }

    private static int ParseMajorVersion(string version)
    {
        var parts = version.Split('.');
        return parts.Length >= 1 && int.TryParse(parts[0], out var major) ? major : 0;
    }

    private static async Task ExecuteAsync(ISshService ssh, string command, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var result = await ssh.ExecuteAsync(command, timeout, ct);
        if (!result.Success)
            throw new InvalidOperationException($"Command failed (exit {result.ExitCode}): {command}\n{result.Stderr}");
    }

    private enum InstallStrategy { UbuntuNative, UbuntuBackports, MicrosoftFeed }
}
