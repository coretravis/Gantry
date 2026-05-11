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
        var appName = context.Config.App.Name;
        var deployUser = context.Config.Server.DeployUser;

        if (!context.IsDryRun)
        {
            var hasOldLayout = await _ssh.DirectoryExistsAsync($"/var/www/{appName}/app", ct);
            var checkResult = await _ssh.ExecuteAsync(
                $"test -L /var/www/{appName}/current && echo yes || echo no", ct: ct);
            var hasCurrentLink = checkResult.Stdout.Trim() == "yes";

            if (hasOldLayout && !hasCurrentLink)
            {
                Report(context, PhaseStatus.Running, "Migrating to symlink release layout...");
                var migrationId = $"pre-symlink-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
                await RunCommandAsync(_ssh,
                    $"mv /var/www/{appName}/app /var/www/{appName}/releases/{migrationId} && " +
                    $"ln -sfn /var/www/{appName}/releases/{migrationId} /var/www/{appName}/current && " +
                    $"mkdir -p /var/www/{appName}/shared && " +
                    $"mv /var/www/{appName}/.env /var/www/{appName}/shared/.env 2>/dev/null || true",
                    context, ct: ct);
            }
        }

        Report(context, PhaseStatus.Running, $"Creating deploy directory /var/www/{appName}...");
        await RunCommandAsync(_ssh,
            $"mkdir -p /var/www/{appName}/releases /var/www/{appName}/shared && " +
            $"touch /var/www/{appName}/shared/.env && " +
            $"chmod 600 /var/www/{appName}/shared/.env && " +
            $"chown -R {deployUser}:{deployUser} /var/www/{appName}",
            context, ct: ct);

        Report(context, PhaseStatus.Running, $"Creating systemd service for {appName}...");
        await _processManager.CreateServiceAsync(_ssh, context.Config, context.IsDryRun, ct);
    }

    protected override async Task RunRollbackAsync(ProvisioningContext context, CancellationToken ct)
    {
        if (!context.IsDryRun)
            await _processManager.RollbackAsync(_ssh, context.Config, ct);
    }
}
