using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Gantry.Core.Phases;
using Microsoft.Extensions.Logging;

namespace Gantry.Infrastructure.Plugins.Postgres;

public class PostgresInstallPhase : PluginPhaseBase
{
    public PostgresInstallPhase(ISshService ssh, ILogger<PostgresInstallPhase> logger)
        : base(ssh, logger) { }

    public override string Name => "postgres-install";
    public override string Description => "Install PostgreSQL database server";
    public override int Order => 25;
    public override string PluginName => "postgres";

    protected override Task ValidatePrerequisitesAsync(ProvisioningContext context, CancellationToken ct)
    {
        if (!context.IsDryRun && context.ServerInfo != null && context.ServerInfo.TotalMemoryMb < 2048)
        {
            Logger.LogWarning(
                "PostgreSQL recommends at least 2 GB RAM. This server has {MemMb} MB - performance may be degraded.",
                context.ServerInfo.TotalMemoryMb);
            Report(context, PhaseStatus.Warning,
                $"Memory advisory: {context.ServerInfo.TotalMemoryMb} MB available, 2048 MB recommended for PostgreSQL");
        }

        return Task.CompletedTask;
    }

    protected override async Task RunAsync(ProvisioningContext context, CancellationToken ct)
    {
        var pg = PostgresPluginConfig.From(context.Config.GetPlugin(PluginName), context.Config.App.Name);

        Report(context, PhaseStatus.Running, $"Installing PostgreSQL {pg.Version}...");
        await RunCommandAsync(_ssh,
            $"dpkg -s postgresql-{pg.Version} &>/dev/null || " +
            $"(DEBIAN_FRONTEND=noninteractive apt-get install -y postgresql-{pg.Version} postgresql-client-{pg.Version})",
            context, timeout: TimeSpan.FromMinutes(5), ct: ct);

        Report(context, PhaseStatus.Running, "Enabling PostgreSQL service...");
        await RunCommandAsync(_ssh, "systemctl enable --now postgresql", context, ct: ct);
    }

    protected override async Task RunRollbackAsync(ProvisioningContext context, CancellationToken ct)
    {
        if (context.IsDryRun) return;

        var pg = PostgresPluginConfig.From(context.Config.GetPlugin(PluginName), context.Config.App.Name);
        Logger.LogInformation("Rolling back postgres-install: removing PostgreSQL {Version}", pg.Version);

        await _ssh.ExecuteAsync("systemctl stop postgresql 2>/dev/null || true", ct: ct);
        await _ssh.ExecuteAsync(
            $"DEBIAN_FRONTEND=noninteractive apt-get remove -y --purge " +
            $"postgresql-{pg.Version} postgresql-client-{pg.Version} postgresql-common 2>/dev/null || true",
            ct: ct);
    }
}
