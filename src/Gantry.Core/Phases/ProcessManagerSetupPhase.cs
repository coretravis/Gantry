using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gantry.Core.Phases;

public class ProcessManagerSetupPhase : PhaseBase
{
    private readonly ISshService _ssh;
    private readonly IProcessManager _processManager;

    public ProcessManagerSetupPhase(ISshService ssh, IProcessManager processManager, ILogger<ProcessManagerSetupPhase> logger)
        : base(logger)
    {
        _ssh = ssh;
        _processManager = processManager;
    }

    public override string Name => "process-manager-setup";
    public override string Description => "Create and enable systemd service";
    public override int Order => 50;

    protected override async Task ValidatePrerequisitesAsync(ProvisioningContext context, CancellationToken ct)
    {
        if (context.IsDryRun) return;

        var deployUser = context.Config.Server.DeployUser;
        var result = await _ssh.ExecuteAsync($"id {deployUser} 2>/dev/null", ct: ct);
        if (!result.Success)
            throw new PhaseException(Name,
                $"Deploy user '{deployUser}' does not exist on the server.",
                "Run: gantry provision --phase os-hardening to create the deploy user first.");
    }

    protected override async Task RunAsync(ProvisioningContext context, CancellationToken ct)
    {
        Report(context, PhaseStatus.Running, $"Creating deploy directory /var/www/{context.Config.App.Name}...");
        await RunCommandAsync(_ssh,
            $"mkdir -p /var/www/{context.Config.App.Name}/app /var/www/{context.Config.App.Name}/releases && chown -R {context.Config.Server.DeployUser}:{context.Config.Server.DeployUser} /var/www/{context.Config.App.Name}",
            context, ct: ct);

        Report(context, PhaseStatus.Running, $"Creating systemd service for {context.Config.App.Name}...");
        await _processManager.CreateServiceAsync(_ssh, context.Config, context.IsDryRun, ct);
    }

    protected override async Task RunRollbackAsync(ProvisioningContext context, CancellationToken ct)
    {
        if (!context.IsDryRun)
            await _processManager.RollbackAsync(_ssh, context.Config, ct);
    }
}
