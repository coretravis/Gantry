using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gantry.Core.Phases;

public class RuntimeInstallationPhase : PhaseBase
{
    private readonly ISshService _ssh;
    private readonly IEnumerable<IRuntimeProvider> _providers;

    public RuntimeInstallationPhase(ISshService ssh, IEnumerable<IRuntimeProvider> providers, ILogger<RuntimeInstallationPhase> logger)
        : base(logger)
    {
        _ssh = ssh;
        _providers = providers;
    }

    public override string Name => "runtime-installation";
    public override string Description => "Install application runtime";
    public override int Order => 30;

    protected override async Task RunAsync(ProvisioningContext context, CancellationToken ct)
    {
        var runtime = context.Config.Runtime;
        var provider = _providers.FirstOrDefault(p => p.Ecosystem.Equals(runtime.Ecosystem, StringComparison.OrdinalIgnoreCase))
            ?? throw new PhaseException(Name, $"No runtime provider found for ecosystem '{runtime.Ecosystem}'.",
                $"Supported ecosystems: {string.Join(", ", _providers.Select(p => p.Ecosystem))}");

        if (!provider.SupportedVersions.Contains(runtime.Version))
            throw new PhaseException(Name,
                $"Runtime version '{runtime.Version}' is not supported for ecosystem '{runtime.Ecosystem}'.",
                $"Supported versions: {string.Join(", ", provider.SupportedVersions)}");

        // Early OS-specific compatibility check - fails before any SSH commands are issued
        var osVersion = context.Config.Server.OsVersion;
        if (!string.IsNullOrEmpty(osVersion) && provider is IOsVersionAwareProvider osAware)
        {
            if (!osAware.IsVersionSupportedOnOs(runtime.Version, osVersion))
            {
                var supportedForOs = osAware.SupportedVersionsForOs(osVersion);
                throw new PhaseException(Name,
                    $".NET {runtime.Version} is not supported on Ubuntu {osVersion}.",
                    $"Supported .NET versions for Ubuntu {osVersion}: {string.Join(", ", supportedForOs)}");
            }
        }

        Report(context, PhaseStatus.Running, $"Checking if {runtime.Ecosystem} {runtime.Version} is already installed...");

        if (!context.IsDryRun && await provider.IsInstalledAsync(_ssh, runtime.Version, ct))
        {
            Logger.LogInformation("{Ecosystem} {Version} is already installed, skipping", runtime.Ecosystem, runtime.Version);
            Report(context, PhaseStatus.Running, $"{runtime.Ecosystem} {runtime.Version} already installed.");
            return;
        }

        Report(context, PhaseStatus.Running, $"Installing {runtime.Ecosystem} {runtime.Version}...");
        await provider.InstallAsync(_ssh, runtime.Version, context.IsDryRun, ct);

        if (!context.IsDryRun)
        {
            var verified = await provider.IsInstalledAsync(_ssh, runtime.Version, ct);
            if (!verified)
                throw new PhaseException(Name, $"Runtime installation succeeded but verification failed for {runtime.Ecosystem} {runtime.Version}.",
                    "Check the server logs and try running 'gantry provision --phase runtime-installation'.");

            Logger.LogInformation("{Ecosystem} {Version} installed and verified", runtime.Ecosystem, runtime.Version);
        }
    }

    protected override async Task RunRollbackAsync(ProvisioningContext context, CancellationToken ct)
    {
        var runtime = context.Config.Runtime;
        var provider = _providers.FirstOrDefault(p => p.Ecosystem.Equals(runtime.Ecosystem, StringComparison.OrdinalIgnoreCase));
        if (provider != null && !context.IsDryRun)
            await provider.RollbackAsync(_ssh, runtime.Version, ct);
    }
}
