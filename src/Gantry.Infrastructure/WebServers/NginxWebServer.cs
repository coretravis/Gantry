using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gantry.Infrastructure.WebServers;

public class NginxWebServer : IWebServer
{
    private readonly ITemplateEngine _templates;
    private readonly ILogger<NginxWebServer> _logger;

    public NginxWebServer(ITemplateEngine templates, ILogger<NginxWebServer> logger)
    {
        _templates = templates;
        _logger = logger;
    }

    public string ServerType => "nginx";

    public async Task InstallAsync(ISshService ssh, bool isDryRun, CancellationToken ct = default)
    {
        _logger.LogInformation("Installing nginx");
        if (!isDryRun)
        {
            var result = await ssh.ExecuteAsync("apt-get install -y nginx", ct: ct);
            if (!result.Success)
                throw new PhaseException("web-server-configuration", $"nginx installation failed: {result.Stderr}");
        }
    }

    public async Task<bool> ConfigureAsync(ISshService ssh, DeployConfig config, bool isDryRun, CancellationToken ct = default)
    {
        var appName = config.App.Name;
        var serverName = config.Domain.HasDomain
            ? (config.Domain.Www ? $"{config.Domain.Name} www.{config.Domain.Name}" : config.Domain.Name)
            : config.Server.Host;

        var nginxConfig = _templates.Render("nginx-site.conf", new Dictionary<string, string>
        {
            ["server_name"] = serverName,
            ["app_port"] = config.App.Port.ToString()
        });

        _logger.LogDebug("Generated nginx config for {AppName}", appName);

        if (isDryRun)
        {
            _logger.LogDebug("[dry-run] Would configure nginx for {AppName}", appName);
            return true;
        }

        var sitePath = $"/etc/nginx/sites-available/{appName}";
        var changed = false;

        if (await ssh.FileExistsAsync(sitePath, ct))
        {
            var existing = await ssh.DownloadStringAsync(sitePath, ct);
            if (existing == nginxConfig)
                _logger.LogDebug("nginx config for {AppName} unchanged, skipping upload", appName);
            else
            {
                await ssh.UploadStringAsync(nginxConfig, sitePath, ct);
                changed = true;
                _logger.LogDebug("nginx config for {AppName} updated", appName);
            }
        }
        else
        {
            await ssh.UploadStringAsync(nginxConfig, sitePath, ct);
            changed = true;
            _logger.LogDebug("nginx config for {AppName} created", appName);
        }

        // Symlink and default-site removal are idempotent; always ensure correct state
        var linkResult = await ssh.ExecuteAsync(
            $"ln -sf /etc/nginx/sites-available/{appName} /etc/nginx/sites-enabled/{appName} && rm -f /etc/nginx/sites-enabled/default",
            ct: ct);
        if (!linkResult.Success)
            throw new PhaseException("web-server-configuration", $"Failed to enable nginx site: {linkResult.Stderr}");

        if (changed)
        {
            var testResult = await ssh.ExecuteAsync("nginx -t", ct: ct);
            if (!testResult.Success)
                throw new PhaseException("web-server-configuration",
                    $"nginx configuration test failed:\n{testResult.Stderr}",
                    "Fix the nginx configuration and retry: gantry provision --phase web-server-configuration");
        }

        return changed;
    }

    public async Task ReloadAsync(ISshService ssh, bool isDryRun, CancellationToken ct = default)
    {
        if (!isDryRun)
        {
            var result = await ssh.ExecuteAsync("systemctl reload nginx", ct: ct);
            if (!result.Success)
                throw new PhaseException("web-server-configuration", $"nginx reload failed: {result.Stderr}");
        }
    }

    public async Task RollbackAsync(ISshService ssh, DeployConfig config, CancellationToken ct = default)
    {
        var appName = config.App.Name;
        _logger.LogInformation("Rolling back nginx configuration for {AppName}", appName);
        await ssh.ExecuteAsync($"rm -f /etc/nginx/sites-available/{appName} /etc/nginx/sites-enabled/{appName}", ct: ct);
        await ssh.ExecuteAsync("systemctl reload nginx 2>/dev/null || true", ct: ct);
    }
}
