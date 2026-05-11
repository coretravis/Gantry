using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gantry.Infrastructure.Plugins.Postgres;

/// <summary>
/// Optional post-deploy hook that runs a configurable migration command on the server.
/// Disabled by default. Enable by setting run_migrations=true and migration_command
/// in the postgres plugin config in .deploy.yml.
/// </summary>
public class PostgresMigrationHook : IPreDeployHook
{
    private readonly ILogger<PostgresMigrationHook> _logger;

    public PostgresMigrationHook(ILogger<PostgresMigrationHook> logger)
    {
        _logger = logger;
    }

    public string PluginName => "postgres";

    public async Task RunAsync(ISshService ssh, DeployConfig config, CancellationToken ct = default)
    {
        var pluginConfig = config.GetPlugin(PluginName);
        if (!pluginConfig.IsEnabled) return;

        var runMigrations = pluginConfig.GetOrDefault("run_migrations", "false");
        if (!runMigrations.Equals("true", StringComparison.OrdinalIgnoreCase)) return;

        var migrationCommand = pluginConfig.Get("migration_command");
        if (string.IsNullOrWhiteSpace(migrationCommand))
        {
            _logger.LogWarning(
                "postgres plugin has run_migrations=true but migration_command is not set. Skipping migrations.");
            return;
        }

        var deployPath = $"/var/www/{config.App.Name}/current";

        _logger.LogInformation("Running database migrations: {Command}", migrationCommand);

        var result = await ssh.ExecuteAsync(
            $"cd {deployPath} && {migrationCommand}",
            timeout: TimeSpan.FromMinutes(5),
            ct: ct);

        if (!result.Success)
            throw new GantryException(
                $"Database migration failed (exit {result.ExitCode}):\n{result.Stderr}",
                "Fix the migration error and re-deploy: gantry deploy");

        _logger.LogInformation("Database migrations completed successfully");
    }
}
