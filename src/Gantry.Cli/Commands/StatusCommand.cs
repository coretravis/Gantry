using Gantry.Cli.UI;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;
using System.CommandLine;

namespace Gantry.Cli.Commands;

public class StatusCommand : Command
{
    public StatusCommand() : base("status", "Show server and service health") { }
}

public class StatusCommandHandler
{
    private readonly ISshService _ssh;
    private readonly IProcessManager _processManager;
    private readonly IConfigService _configService;
    private readonly ISslProvider _sslProvider;
    private readonly IEnumerable<IStatusContributor> _statusContributors;
    private readonly ILogger<StatusCommandHandler> _logger;

    public StatusCommandHandler(
        ISshService ssh,
        IProcessManager processManager,
        IConfigService configService,
        ISslProvider sslProvider,
        IEnumerable<IStatusContributor> statusContributors,
        ILogger<StatusCommandHandler> logger)
    {
        _ssh = ssh;
        _processManager = processManager;
        _configService = configService;
        _sslProvider = sslProvider;
        _statusContributors = statusContributors;
        _logger = logger;
    }

    public async Task<int> ExecuteAsync(string configPath, CancellationToken ct = default)
    {
        var config = _configService.Load(configPath);
        var server = config.Server;
        var app = config.App;
        var expandedKey = server.DeployKeyPath.Replace("~",
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile));

        try
        {
            await _ssh.ConnectAsync(server.Host, server.DeployUser, expandedKey, server.Port, ct);

            var checks = new List<HealthCheck>();

            // --- Application process ---
            var isActive = await _processManager.IsActiveAsync(_ssh, app.Name, ct);
            checks.Add(isActive
                ? new HealthCheck("Application", HealthStatus.Healthy, "Service active (running)")
                : new HealthCheck("Application", HealthStatus.Critical, "Service inactive or failed",
                    "Run: gantry deploy"));

            // --- Application port ---
            if (isActive)
            {
                var portResult = await _ssh.ExecuteAsync(
                    $"curl -sf --max-time 5 http://localhost:{app.Port}{app.HealthCheckPath} -o /dev/null -w '%{{http_code}}'",
                    ct: ct);
                var statusCode = portResult.Stdout.Trim();
                if (portResult.Success && statusCode.StartsWith("2"))
                    checks.Add(new HealthCheck("App port", HealthStatus.Healthy, $"Responding on port {app.Port} (HTTP {statusCode})"));
                else
                    checks.Add(new HealthCheck("App port", HealthStatus.Critical,
                        $"Not responding on port {app.Port} (HTTP {statusCode})",
                        $"Check logs with: gantry status, then run: gantry deploy"));
            }
            else
            {
                checks.Add(new HealthCheck("App port", HealthStatus.NotApplicable, "Skipped - service not running"));
            }

            // --- nginx ---
            var nginxActive = await _ssh.ExecuteAsync("systemctl is-active nginx", ct: ct);
            var nginxRunning = nginxActive.Stdout.Trim() == "active";
            checks.Add(nginxRunning
                ? new HealthCheck("nginx", HealthStatus.Healthy, "Service active")
                : new HealthCheck("nginx", HealthStatus.Critical, "nginx is inactive",
                    "Run: gantry provision --phase web-server-configuration"));

            if (nginxRunning)
            {
                var nginxTest = await _ssh.ExecuteAsync("nginx -t 2>&1", ct: ct);
                checks.Add(nginxTest.Success
                    ? new HealthCheck("nginx config", HealthStatus.Healthy, "Configuration valid")
                    : new HealthCheck("nginx config", HealthStatus.Warning,
                        $"Configuration test failed: {nginxTest.Stdout.Trim().Split('\n').FirstOrDefault()}",
                        "Run: gantry provision --phase web-server-configuration"));
            }

            // --- SSL ---
            if (config.Domain.HasDomain && config.Domain.Ssl)
            {
                DateTimeOffset? expiry = null;
                try { expiry = await _sslProvider.GetExpiryAsync(_ssh, config.Domain.Name, ct); }
                catch (Exception ex) { _logger.LogDebug("SSL expiry check failed: {Error}", ex.Message); }

                if (expiry == null)
                    checks.Add(new HealthCheck("SSL certificate", HealthStatus.Warning,
                        $"Could not determine certificate expiry for {config.Domain.Name}",
                        "Run: gantry provision --phase ssl-provisioning"));
                else if (expiry.Value < DateTimeOffset.UtcNow)
                    checks.Add(new HealthCheck("SSL certificate", HealthStatus.Critical,
                        $"Certificate expired on {expiry.Value:yyyy-MM-dd}",
                        "Run: gantry provision --phase ssl-provisioning"));
                else if (expiry.Value < DateTimeOffset.UtcNow.AddDays(30))
                    checks.Add(new HealthCheck("SSL certificate", HealthStatus.Warning,
                        $"Certificate expires soon: {expiry.Value:yyyy-MM-dd}",
                        "Run: gantry provision --phase ssl-provisioning"));
                else
                    checks.Add(new HealthCheck("SSL certificate", HealthStatus.Healthy,
                        $"Valid until {expiry.Value:yyyy-MM-dd}"));

                // DNS check
                try
                {
                    var dnsResult = await _ssh.ExecuteAsync(
                        $"dig +short {config.Domain.Name} 2>/dev/null || nslookup {config.Domain.Name} 2>/dev/null | grep Address | tail -1 | awk '{{print $2}}'",
                        ct: ct);
                    var resolved = dnsResult.Stdout.Trim().Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
                    if (string.IsNullOrEmpty(resolved))
                        checks.Add(new HealthCheck("DNS", HealthStatus.Warning,
                            $"{config.Domain.Name} does not resolve",
                            "Check your DNS A record points to this server."));
                    else if (resolved != server.Host)
                        checks.Add(new HealthCheck("DNS", HealthStatus.Warning,
                            $"{config.Domain.Name} resolves to {resolved}, expected {server.Host}",
                            "Update your DNS A record to point to this server."));
                    else
                        checks.Add(new HealthCheck("DNS", HealthStatus.Healthy,
                            $"{config.Domain.Name} → {resolved}"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("DNS check failed: {Error}", ex.Message);
                    checks.Add(new HealthCheck("DNS", HealthStatus.NotApplicable, "DNS tools not available on server"));
                }
            }
            else
            {
                checks.Add(new HealthCheck("SSL certificate", HealthStatus.NotApplicable, "No domain configured"));
                checks.Add(new HealthCheck("DNS", HealthStatus.NotApplicable, "No domain configured"));
            }

            // --- Disk ---
            var diskResult = await _ssh.ExecuteAsync(
                $"df -BG /var/www/{app.Name}/ 2>/dev/null | awk 'NR==2{{print $4}}'",
                ct: ct);
            var freeGbStr = diskResult.Stdout.Trim().TrimEnd('G');
            if (long.TryParse(freeGbStr, out var freeGb))
            {
                checks.Add(freeGb < 1
                    ? new HealthCheck("Disk", HealthStatus.Warning,
                        $"{freeGb}GB free in /var/www/{app.Name}/",
                        $"Clean up old releases or reduce releases_to_keep in .deploy.yml")
                    : new HealthCheck("Disk", HealthStatus.Healthy, $"{freeGb}GB free"));
            }
            else
            {
                checks.Add(new HealthCheck("Disk", HealthStatus.NotApplicable, "Could not read disk usage"));
            }

            // --- Plugin checks ---
            foreach (var contributor in _statusContributors)
            {
                try
                {
                    var pluginChecks = await contributor.GetHealthAsync(_ssh, config, ct);
                    checks.AddRange(pluginChecks);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Plugin '{Plugin}' health check failed: {Error}", contributor.PluginName, ex.Message);
                    checks.Add(new HealthCheck(
                        $"{contributor.PluginName} (plugin)", HealthStatus.Warning,
                        "Plugin health check unavailable - check server connectivity"));
                }
            }

            // --- Logs ---
            var logs = await _processManager.GetLogsAsync(_ssh, app.Name, 20, ct);
            var status = await _processManager.GetStatusAsync(_ssh, app.Name, ct);

            var report = new HealthReport(app.Name, server.Host, checks);
            ConsoleRenderer.ShowHealthReport(report, status, logs);

            return report.ExitCode;
        }
        finally
        {
            await _ssh.DisposeAsync();
        }
    }
}
