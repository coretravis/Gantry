using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gantry.Infrastructure.Plugins.Postgres;

public class PostgresStatusContributor : IStatusContributor
{
    private readonly ILogger<PostgresStatusContributor> _logger;

    public PostgresStatusContributor(ILogger<PostgresStatusContributor> logger)
    {
        _logger = logger;
    }

    public string PluginName => "postgres";

    public async Task<IReadOnlyList<HealthCheck>> GetHealthAsync(ISshService ssh, DeployConfig config, CancellationToken ct = default)
    {
        var pluginConfig = config.GetPlugin(PluginName);
        if (!pluginConfig.IsEnabled)
            return Array.Empty<HealthCheck>();

        var results = new List<HealthCheck>();

        // Service status
        var serviceResult = await ssh.ExecuteAsync("systemctl is-active postgresql 2>/dev/null", ct: ct);
        var isActive = serviceResult.Stdout.Trim() == "active";

        results.Add(isActive
            ? new HealthCheck("PostgreSQL", HealthStatus.Healthy, "Service active")
            : new HealthCheck("PostgreSQL", HealthStatus.Critical, "Service inactive",
                "Run: gantry provision --phase postgres-install"));

        if (isActive)
        {
            // Connection check
            var connResult = await ssh.ExecuteAsync(
                "sudo -u postgres psql -t -q -c 'SELECT 1;' 2>&1",
                ct: ct);
            results.Add(connResult.Success
                ? new HealthCheck("PostgreSQL connections", HealthStatus.Healthy, "Accepting connections")
                : new HealthCheck("PostgreSQL connections", HealthStatus.Warning,
                    "Cannot query PostgreSQL",
                    "Run: gantry provision --phase postgres-configure"));

            // Disk space for PostgreSQL data directory
            try
            {
                var dataDir = await ssh.ExecuteAsync(
                    "sudo -u postgres psql -t -q -c 'SHOW data_directory;' 2>/dev/null | tr -d ' \\n'",
                    ct: ct);
                if (!string.IsNullOrWhiteSpace(dataDir.Stdout))
                {
                    var diskResult = await ssh.ExecuteAsync(
                        $"df -BG {dataDir.Stdout.Trim()} 2>/dev/null | awk 'NR==2{{print $4}}'",
                        ct: ct);
                    var freeGbStr = diskResult.Stdout.Trim().TrimEnd('G');
                    if (long.TryParse(freeGbStr, out var freeGb) && freeGb < 2)
                        results.Add(new HealthCheck("PostgreSQL disk", HealthStatus.Warning,
                            $"{freeGb} GB free in PostgreSQL data directory",
                            "Consider expanding disk or archiving old data"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("PostgreSQL disk check failed: {Error}", ex.Message);
            }
        }

        // Memory advisory
        try
        {
            var memResult = await ssh.ExecuteAsync("free -m | awk 'NR==2{print $2}'", ct: ct);
            if (long.TryParse(memResult.Stdout.Trim(), out var totalMb) && totalMb < 2048)
                results.Add(new HealthCheck("PostgreSQL memory", HealthStatus.Warning,
                    $"Server has {totalMb} MB RAM - 2048 MB recommended for PostgreSQL + application"));
        }
        catch (Exception ex)
        {
            _logger.LogDebug("PostgreSQL memory check failed: {Error}", ex.Message);
        }

        return results;
    }
}
