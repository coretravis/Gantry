using Gantry.Cli.UI;
using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.CommandLine;
using System.Diagnostics;

namespace Gantry.Cli.Commands;

public class DeployCommand : Command
{
    public DeployCommand() : base("deploy", "Build and deploy the application to the server") { }
}

public class DeployCommandHandler
{
    private readonly ISshService _ssh;
    private readonly IProcessManager _processManager;
    private readonly IConfigService _configService;
    private readonly IEnumerable<IDeployHook> _deployHooks;
    private readonly ILogger<DeployCommandHandler> _logger;

    public DeployCommandHandler(
        ISshService ssh,
        IProcessManager processManager,
        IConfigService configService,
        IEnumerable<IDeployHook> deployHooks,
        ILogger<DeployCommandHandler> logger)
    {
        _ssh = ssh;
        _processManager = processManager;
        _configService = configService;
        _deployHooks = deployHooks;
        _logger = logger;
    }

    public async Task<int> ExecuteAsync(string configPath, bool dryRun, CancellationToken ct = default)
    {
        var config = _configService.Load(configPath);
        var sw = Stopwatch.StartNew();
        var releaseId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");

        _logger.LogInformation("Starting deploy release {ReleaseId}", releaseId);

        try
        {
            var publishDir = await BuildAndPublishAsync(config, dryRun, ct);

            if (!dryRun)
                ValidatePublishOutput(publishDir, config);

            var server = config.Server;
            var expandedKey = server.DeployKeyPath.Replace("~", System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile));

            if (!dryRun)
                await _ssh.ConnectAsync(server.Host, server.DeployUser, expandedKey, server.Port, ct);

            var deployPath = string.IsNullOrWhiteSpace(config.App.DeployPath)
                ? $"/var/www/{config.App.Name}/app"
                : config.App.DeployPath;

            await SnapshotCurrentReleaseAsync(config, releaseId, deployPath, dryRun, ct);
            await TransferFilesAsync(publishDir, deployPath, config, dryRun, ct);
            await RestartServiceAsync(config, dryRun, ct);
            await RunDeployHooksAsync(config, dryRun, ct);
            await HealthCheckAsync(config, dryRun, ct);

            sw.Stop();
            ConsoleRenderer.ShowSuccess($"Deploy complete in {sw.Elapsed.TotalSeconds:F1}s");
            ConsoleRenderer.ShowSummary("Deploy Summary", new[]
            {
                ("Release", releaseId),
                ("Duration", $"{sw.Elapsed.TotalSeconds:F1}s"),
                ("Service", $"{config.App.Name}.service"),
                ("Health check", "passed")
            });

            return 0;
        }
        catch (GantryException ex)
        {
            ConsoleRenderer.ShowError(ex.Message);
            if (ex.Remediation != null)
            {
                ConsoleRenderer.ShowInfo($"Suggested fix: {ex.Remediation}");
            }

            if (_ssh.IsConnected)
            {
                try
                {
                    var logs = await _processManager.GetLogsAsync(_ssh, config.App.Name, 30, ct);
                    if (!string.IsNullOrWhiteSpace(logs))
                    {
                        AnsiConsole.WriteLine();
                        AnsiConsole.Write(new Rule("[bold]Service Logs[/]"));
                        AnsiConsole.WriteLine(logs);
                    }
                }
                catch { /* best effort */ }
            }

            return 1;
        }
        finally
        {
            await _ssh.DisposeAsync();
        }
    }

    private async Task<string> BuildAndPublishAsync(Core.Models.DeployConfig config, bool dryRun, CancellationToken ct)
    {
        var publishDir = Path.Combine(Path.GetTempPath(), $"gantry-publish-{Guid.NewGuid():N}");

        await AnsiConsole.Status().StartAsync("Building...", async ctx =>
        {
            if (!dryRun)
            {
                Directory.CreateDirectory(publishDir);
                var projectPath = ResolveProjectPath(config.App.ProjectPath);
                var psi = new ProcessStartInfo("dotnet",
                    $"publish \"{projectPath}\" -c Release -o \"{publishDir}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi) ?? throw new GantryException("Failed to start dotnet publish.");
                var stderrTask = proc.StandardError.ReadToEndAsync(ct);

                string? line;
                while ((line = await proc.StandardOutput.ReadLineAsync(ct)) != null)
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        ctx.Status($"Building: {Markup.Escape(trimmed.Length > 60 ? trimmed[..60] + "..." : trimmed)}");
                }

                await proc.WaitForExitAsync(ct);
                var stderr = await stderrTask;
                if (proc.ExitCode != 0)
                    throw new GantryException($"dotnet publish failed (exit {proc.ExitCode}):\n{stderr}");
                _logger.LogInformation("Published to {Dir}", publishDir);
            }
            else
            {
                _logger.LogDebug("[dry-run] Would publish {Project}", config.App.ProjectPath);
            }
        });

        return publishDir;
    }

    private async Task SnapshotCurrentReleaseAsync(Core.Models.DeployConfig config, string releaseId, string deployPath, bool dryRun, CancellationToken ct)
    {
        if (dryRun) return;
        var releasesPath = $"/var/www/{config.App.Name}/releases/{releaseId}";
        await _ssh.ExecuteAsync($"mkdir -p {releasesPath} && cp -r {deployPath}/. {releasesPath}/ 2>/dev/null || true", ct: ct);
        await PruneOldReleasesAsync(config, dryRun, ct);
    }

    private async Task PruneOldReleasesAsync(Core.Models.DeployConfig config, bool dryRun, CancellationToken ct)
    {
        if (dryRun) return;
        var keep = config.App.ReleasesToKeep;
        await _ssh.ExecuteAsync(
            $"ls -1dt /var/www/{config.App.Name}/releases/*/ 2>/dev/null | tail -n +{keep + 1} | xargs rm -rf",
            ct: ct);
    }

    private async Task TransferFilesAsync(string publishDir, string deployPath, Core.Models.DeployConfig config, bool dryRun, CancellationToken ct)
    {
        await AnsiConsole.Status().StartAsync("Packing archive...", async ctx =>
        {
            if (!dryRun)
            {
                var archivePath = Path.Combine(Path.GetTempPath(), $"gantry-{Guid.NewGuid():N}.tar.gz");
                try
                {
                    await CreateTarGzAsync(publishDir, archivePath, ct);

                    var bytes = new FileInfo(archivePath).Length;
                    var sizeStr = bytes >= 1024 * 1024 ? $"{bytes / (1024.0 * 1024):F1} MB" : $"{bytes / 1024:F0} KB";

                    ctx.Status($"Uploading {sizeStr}...");
                    var remoteTar = $"/tmp/gantry-deploy-{Guid.NewGuid():N}.tar.gz";
                    await _ssh.UploadFileAsync(archivePath, remoteTar, ct);

                    ctx.Status("Extracting on server...");
                    await _ssh.ExecuteAsync($"mkdir -p {deployPath} && tar xzf {remoteTar} -C {deployPath} && rm -f {remoteTar}", ct: ct);
                    _logger.LogInformation("Transferred {Size} archive to {DeployPath}", sizeStr, deployPath);
                }
                finally
                {
                    if (File.Exists(archivePath))
                        File.Delete(archivePath);
                }
            }
            else
            {
                _logger.LogDebug("[dry-run] Would transfer publish output to {Path}", deployPath);
            }
        });
    }

    // If the stored path isn't found relative to the current directory (e.g. user is in project
    // subdir but path is repo-root-relative), try resolving it from the git root instead.
    private static string ResolveProjectPath(string projectPath)
    {
        if (File.Exists(projectPath)) return projectPath;

        try
        {
            var psi = new ProcessStartInfo("git", "rev-parse --show-toplevel")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var stdoutTask = Task.Run(() => proc.StandardOutput.ReadToEnd());
                proc.WaitForExit();
                var gitRoot = stdoutTask.Result.Trim();
                if (proc.ExitCode == 0 && !string.IsNullOrEmpty(gitRoot))
                {
                    var fromRoot = Path.Combine(gitRoot, projectPath);
                    if (File.Exists(fromRoot)) return fromRoot;
                }
            }
        }
        catch { }

        return projectPath;
    }

    private static void ValidatePublishOutput(string publishDir, Core.Models.DeployConfig config)
    {
        var assemblyName = Path.GetFileNameWithoutExtension(config.App.ProjectPath) is { Length: > 0 } n ? n : config.App.Name;
        var expectedDll = Path.Combine(publishDir, $"{assemblyName}.dll");
        if (!File.Exists(expectedDll))
            throw new GantryException(
                $"Expected assembly '{assemblyName}.dll' was not found in publish output.",
                $"Verify project_path in .deploy.yml points to the correct .csproj. If your assembly name differs from the project file name, update app.name to match.");
    }

    private static async Task CreateTarGzAsync(string sourceDir, string archivePath, CancellationToken ct)
    {
        await using var fileStream = File.Create(archivePath);
        await using var gzip = new System.IO.Compression.GZipStream(fileStream, System.IO.Compression.CompressionMode.Compress);
        await System.Formats.Tar.TarFile.CreateFromDirectoryAsync(sourceDir, gzip, includeBaseDirectory: false, cancellationToken: ct);
    }

    private async Task RunDeployHooksAsync(Core.Models.DeployConfig config, bool dryRun, CancellationToken ct)
    {
        foreach (var hook in _deployHooks)
        {
            var pluginConfig = config.GetPlugin(hook.PluginName);
            if (!pluginConfig.IsEnabled) continue;

            _logger.LogInformation("Running deploy hook for plugin '{Plugin}'", hook.PluginName);
            if (!dryRun)
                await hook.RunAsync(_ssh, config, ct);
            else
                _logger.LogDebug("[dry-run] Would run deploy hook for plugin '{Plugin}'", hook.PluginName);
        }
    }

    private async Task RestartServiceAsync(Core.Models.DeployConfig config, bool dryRun, CancellationToken ct)
    {
        await AnsiConsole.Status().StartAsync($"Restarting {config.App.Name}.service...", async _ =>
        {
            if (!dryRun)
            {
                await _processManager.RestartAsync(_ssh, config.App.Name, ct);
                var retries = 6;
                for (var i = 0; i < retries; i++)
                {
                    await Task.Delay(2000, ct);
                    if (await _processManager.IsActiveAsync(_ssh, config.App.Name, ct))
                    {
                        _logger.LogInformation("Service {Name} is active", config.App.Name);
                        return;
                    }
                }
                throw new GantryException($"Service {config.App.Name} did not become active after restart.",
                    $"Check logs: gantry status --config {config.App.Name}");
            }
        });
    }

    private async Task HealthCheckAsync(Core.Models.DeployConfig config, bool dryRun, CancellationToken ct)
    {
        if (dryRun) return;

        var url = config.Domain.HasDomain
            ? $"https://{config.Domain.Name}{config.App.HealthCheckPath}"
            : $"http://{config.Server.Host}:{config.App.Port}{config.App.HealthCheckPath}";

        await AnsiConsole.Status().StartAsync($"Health check {url}...", async _ =>
        {
            using var http = new System.Net.Http.HttpClient
            {
                Timeout = TimeSpan.FromSeconds(config.App.HealthCheckTimeoutSeconds)
            };

            for (var i = 1; i <= 6; i++)
            {
                try
                {
                    var response = await http.GetAsync(url, ct);
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("Health check passed: {Url} → {Status}", url, (int)response.StatusCode);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Health check attempt {I} failed: {Error}", i, ex.Message);
                }
                if (i < 6) await Task.Delay(5000, ct);
            }

            throw new GantryException($"Health check failed for {url}.", "Check service logs: gantry status");
        });
    }
}
