using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gantry.Core.Phases;

public class ConnectAndVerifyPhase : PhaseBase
{
    private readonly ISshService _ssh;

    public ConnectAndVerifyPhase(ISshService ssh, ILogger<ConnectAndVerifyPhase> logger)
        : base(logger) => _ssh = ssh;

    public override string Name => "connect-and-verify";
    public override string Description => "Connect to server and verify access";
    public override int Order => 10;

    protected override async Task RunAsync(ProvisioningContext context, CancellationToken ct)
    {
        var server = context.Config.Server;

        Report(context, PhaseStatus.Running, $"Connecting to {server.Host} as {server.SshUser}...");

        if (!context.IsDryRun)
        {
            var expandedKey = server.SshKeyPath.Replace("~", System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile));

            await _ssh.ConnectAsync(server.Host, server.SshUser, expandedKey, server.Port, ct);
        }

        var whoami = await RunCommandAsync(_ssh, "whoami", context, ct: ct);
        var osInfo = await RunCommandAsync(_ssh, "lsb_release -a 2>/dev/null || cat /etc/os-release", context, ct: ct);
        var memory = await RunCommandAsync(_ssh, "free -m | awk 'NR==2{print $2, $7}'", context, ct: ct);
        var disk = await RunCommandAsync(_ssh, "df -BG / | awk 'NR==2{print $2, $4}'", context, ct: ct);
        var arch = await RunCommandAsync(_ssh, "uname -m", context, ct: ct);
        var hostname = await RunCommandAsync(_ssh, "hostname", context, ct: ct);

        var sudoCheck = context.IsDryRun
            ? CommandResult.DryRun("sudo -n true")
            : await _ssh.ExecuteAsync("sudo -n true", ct: ct);

        if (!context.IsDryRun && !sudoCheck.Success && whoami.Stdout.Trim() != "root")
            throw new PhaseException(Name,
                "The connecting user has no root or passwordless sudo access.",
                "Run: echo 'username ALL=(ALL) NOPASSWD:ALL' | sudo tee /etc/sudoers.d/username");

        var memParts = memory.Stdout.Trim().Split(' ');
        var diskParts = disk.Stdout.Trim().Split(' ');

        context.ServerInfo = new ServerInfo
        {
            Hostname = hostname.Stdout.Trim(),
            OsName = ParseOsName(osInfo.Stdout),
            OsVersion = ParseOsVersion(osInfo.Stdout),
            Architecture = arch.Stdout.Trim(),
            TotalMemoryMb = memParts.Length > 0 && long.TryParse(memParts[0], out var tm) ? tm : 0,
            AvailableMemoryMb = memParts.Length > 1 && long.TryParse(memParts[1], out var am) ? am : 0,
            TotalDiskGb = diskParts.Length > 0 && long.TryParse(diskParts[0].TrimEnd('G'), out var td) ? td : 0,
            AvailableDiskGb = diskParts.Length > 1 && long.TryParse(diskParts[1].TrimEnd('G'), out var ad) ? ad : 0,
            ConnectedUser = whoami.Stdout.Trim(),
            HasRootAccess = whoami.Stdout.Trim() == "root",
            HasPasswordlessSudo = sudoCheck.Success
        };

        // Persist detected OS version so subsequent phases and re-runs can use it without re-querying
        if (string.IsNullOrEmpty(context.Config.Server.OsVersion))
            context.Config.Server.OsVersion = context.ServerInfo.OsVersion;

        Logger.LogInformation("Connected to {Hostname} ({Os} {Version}, {Arch}, {MemMb}MB RAM)",
            context.ServerInfo.Hostname,
            context.ServerInfo.OsName,
            context.ServerInfo.OsVersion,
            context.ServerInfo.Architecture,
            context.ServerInfo.TotalMemoryMb);

        if (!context.IsDryRun &&
            !context.ServerInfo.OsVersion.StartsWith("22.") &&
            !context.ServerInfo.OsVersion.StartsWith("24."))
        {
            Logger.LogWarning("Detected OS version {Version} is not a supported Ubuntu LTS (22.04 or 24.04). Proceeding with caution.", context.ServerInfo.OsVersion);
        }
    }

    private static string ParseOsName(string raw)
    {
        var line = raw.Split('\n').FirstOrDefault(l => l.StartsWith("PRETTY_NAME") || l.StartsWith("Distributor ID")) ?? string.Empty;
        return line.Contains('=') ? line.Split('=')[1].Trim('"', ' ') :
               line.Contains(':') ? line.Split(':')[1].Trim() : "Unknown";
    }

    private static string ParseOsVersion(string raw)
    {
        var line = raw.Split('\n').FirstOrDefault(l => l.StartsWith("VERSION_ID") || l.StartsWith("Release")) ?? string.Empty;
        return line.Contains('=') ? line.Split('=')[1].Trim('"', ' ') :
               line.Contains(':') ? line.Split(':')[1].Trim() : "Unknown";
    }
}
