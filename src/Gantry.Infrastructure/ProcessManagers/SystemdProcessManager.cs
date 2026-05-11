using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gantry.Infrastructure.ProcessManagers;

public class SystemdProcessManager : IProcessManager
{
    private readonly ITemplateEngine _templates;
    private readonly ILogger<SystemdProcessManager> _logger;

    public SystemdProcessManager(ITemplateEngine templates, ILogger<SystemdProcessManager> logger)
    {
        _templates = templates;
        _logger = logger;
    }

    public async Task CreateServiceAsync(ISshService ssh, DeployConfig config, bool isDryRun, CancellationToken ct = default)
    {
        var app = config.App;
        var server = config.Server;
        var deployPath = $"/var/www/{app.Name}/current";

        var assemblyName = Path.GetFileNameWithoutExtension(app.ProjectPath) is { Length: > 0 } n ? n : app.Name;
        var dotnetExec = "/usr/bin/dotnet";
        var envFilePath = $"/var/www/{app.Name}/shared/.env";

        var envVars = config.Environment
            .Select(kv => $"Environment={kv.Key}={kv.Value}")
            .Append($"Environment=ASPNETCORE_URLS=http://127.0.0.1:{app.Port}");

        var serviceContent = _templates.Render("systemd-service.conf", new Dictionary<string, string>
        {
            ["app_description"] = $"{app.Name} ASP.NET Core Application",
            ["deploy_path"] = deployPath,
            ["dotnet_exec"] = dotnetExec,
            ["assembly_name"] = assemblyName,
            ["deploy_user"] = server.DeployUser,
            ["service_name"] = app.Name,
            ["environment_vars"] = string.Join("\n", envVars),
            ["env_file"] = envFilePath
        });

        _logger.LogDebug("Generated systemd service for {AppName}", app.Name);

        if (!isDryRun)
        {
            var servicePath = $"/etc/systemd/system/{app.Name}.service";

            // Upload service file only if content changed
            var serviceChanged = false;
            if (await ssh.FileExistsAsync(servicePath, ct))
            {
                var existing = await ssh.DownloadStringAsync(servicePath, ct);
                if (existing == serviceContent)
                    _logger.LogDebug("Systemd service file for {App} unchanged, skipping upload", app.Name);
                else
                {
                    await ssh.UploadStringAsync(serviceContent, servicePath, ct);
                    serviceChanged = true;
                    _logger.LogDebug("Systemd service file for {App} updated", app.Name);
                }
            }
            else
            {
                await ssh.UploadStringAsync(serviceContent, servicePath, ct);
                serviceChanged = true;
                _logger.LogDebug("Systemd service file for {App} created", app.Name);
            }

            // Upload sudoers entry only if content changed
            var sudoersEntry = $"{server.DeployUser} ALL=(ALL) NOPASSWD: /bin/systemctl restart {app.Name}.service, /bin/systemctl stop {app.Name}.service, /bin/systemctl start {app.Name}.service";
            var sudoersPath = $"/etc/sudoers.d/{app.Name}-deploy";
            if (await ssh.FileExistsAsync(sudoersPath, ct))
            {
                var existing = await ssh.DownloadStringAsync(sudoersPath, ct);
                if (existing.Trim() == sudoersEntry.Trim())
                    _logger.LogDebug("Sudoers entry for {App} unchanged, skipping", app.Name);
                else
                {
                    await ssh.UploadStringAsync(sudoersEntry + "\n", sudoersPath, ct);
                    var visudoCheck = await ssh.ExecuteAsync($"visudo -c -f {sudoersPath}", ct: ct);
                    if (!visudoCheck.Success)
                        throw new PhaseException("process-manager-setup", $"sudoers file validation failed: {visudoCheck.Stderr}");
                    await ssh.ExecuteAsync($"chmod 440 {sudoersPath}", ct: ct);
                }
            }
            else
            {
                await ssh.UploadStringAsync(sudoersEntry + "\n", sudoersPath, ct);
                var visudoCheck = await ssh.ExecuteAsync($"visudo -c -f {sudoersPath}", ct: ct);
                if (!visudoCheck.Success)
                    throw new PhaseException("process-manager-setup", $"sudoers file validation failed: {visudoCheck.Stderr}");
                await ssh.ExecuteAsync($"chmod 440 {sudoersPath}", ct: ct);
            }

            // Create shared/ and the secrets env file only if they don't already exist
            await ssh.ExecuteAsync(
                $"mkdir -p /var/www/{app.Name}/shared && " +
                $"[ -f {envFilePath} ] || (touch {envFilePath} && chmod 600 {envFilePath} && chown {server.DeployUser}:{server.DeployUser} {envFilePath})",
                ct: ct);

            // daemon-reload only when service file changed
            if (serviceChanged)
            {
                var daemonReload = await ssh.ExecuteAsync("systemctl daemon-reload", ct: ct);
                if (!daemonReload.Success)
                    throw new PhaseException("process-manager-setup", $"systemctl daemon-reload failed: {daemonReload.Stderr}");
            }

            // systemctl enable is idempotent; always ensure it is enabled
            var enable = await ssh.ExecuteAsync($"systemctl enable {app.Name}.service", ct: ct);
            if (!enable.Success)
                throw new PhaseException("process-manager-setup", $"Failed to enable service: {enable.Stderr}");

            _logger.LogInformation("Systemd service {AppName}.service {Action}",
                app.Name, serviceChanged ? "created/updated and enabled" : "already up-to-date");
        }
    }

    public async Task StartAsync(ISshService ssh, string serviceName, CancellationToken ct = default)
    {
        var result = await ssh.ExecuteAsync($"sudo systemctl start {serviceName}.service", ct: ct);
        if (!result.Success)
            throw new GantryException($"Failed to start {serviceName}: {result.Stderr}");
    }

    public async Task StopAsync(ISshService ssh, string serviceName, CancellationToken ct = default)
    {
        await ssh.ExecuteAsync($"sudo systemctl stop {serviceName}.service", ct: ct);
    }

    public async Task RestartAsync(ISshService ssh, string serviceName, CancellationToken ct = default)
    {
        var result = await ssh.ExecuteAsync($"sudo systemctl restart {serviceName}.service", ct: ct);
        if (!result.Success)
            throw new GantryException($"Failed to restart {serviceName}: {result.Stderr}");
    }

    public async Task<bool> IsActiveAsync(ISshService ssh, string serviceName, CancellationToken ct = default)
    {
        var result = await ssh.ExecuteAsync($"systemctl is-active {serviceName}.service", ct: ct);
        return result.Stdout.Trim() == "active";
    }

    public async Task<string> GetStatusAsync(ISshService ssh, string serviceName, CancellationToken ct = default)
    {
        var result = await ssh.ExecuteAsync($"systemctl status {serviceName}.service --no-pager", ct: ct);
        return result.Stdout;
    }

    public async Task<string> GetLogsAsync(ISshService ssh, string serviceName, int lines, CancellationToken ct = default)
    {
        var result = await ssh.ExecuteAsync($"journalctl -u {serviceName}.service -n {lines} --no-pager", ct: ct);
        return result.Stdout;
    }

    public async Task RollbackAsync(ISshService ssh, DeployConfig config, CancellationToken ct = default)
    {
        var appName = config.App.Name;
        _logger.LogInformation("Rolling back systemd service for {AppName}", appName);
        await ssh.ExecuteAsync($"systemctl disable {appName}.service 2>/dev/null || true", ct: ct);
        await ssh.ExecuteAsync($"rm -f /etc/systemd/system/{appName}.service /etc/sudoers.d/{appName}-deploy", ct: ct);
        await ssh.ExecuteAsync("systemctl daemon-reload", ct: ct);
    }
}
