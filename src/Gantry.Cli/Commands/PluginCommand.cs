using Gantry.Cli.UI;
using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Gantry.Infrastructure.Plugins;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.CommandLine;

namespace Gantry.Cli.Commands;

public class PluginCommand : Command
{
    public PluginCommand() : base("plugin", "Manage Gantry plugins") { }
}

public class PluginCommandHandler
{
    private readonly IEnumerable<IPhase> _phases;
    private readonly IPhaseOrchestrator _orchestrator;
    private readonly IConfigService _configService;
    private readonly ISshService _ssh;
    private readonly ILogger<PluginCommandHandler> _logger;

    public PluginCommandHandler(
        IEnumerable<IPhase> phases,
        IPhaseOrchestrator orchestrator,
        IConfigService configService,
        ISshService ssh,
        ILogger<PluginCommandHandler> logger)
    {
        _phases = phases;
        _orchestrator = orchestrator;
        _configService = configService;
        _ssh = ssh;
        _logger = logger;
    }

    public Task<int> ListAsync(string configPath, CancellationToken ct = default)
    {
        DeployConfig? config = null;
        try { config = _configService.Load(configPath); } catch { /* config may not exist */ }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold]Available Plugins[/]").LeftJustified());

        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
        table.AddColumn("[bold]Plugin[/]");
        table.AddColumn("[bold]Description[/]");
        table.AddColumn("[bold]Status[/]");

        foreach (var (name, meta) in PluginRegistry.All)
        {
            var enabled = config?.GetPlugin(name).IsEnabled ?? false;
            var (mark, color) = enabled ? ("enabled", "green") : ("disabled", "grey");
            table.AddRow(meta.Name, meta.Description, $"[{color}]{mark}[/]");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        ConsoleRenderer.ShowInfo("To enable a plugin:  gantry plugin add <name>");
        ConsoleRenderer.ShowInfo("To disable a plugin: gantry plugin remove <name>");
        AnsiConsole.WriteLine();

        return Task.FromResult(0);
    }

    public async Task<int> AddAsync(string pluginName, string configPath, bool dryRun, string[] setOptions, CancellationToken ct = default)
    {
        if (!PluginRegistry.All.TryGetValue(pluginName, out var metadata))
        {
            ConsoleRenderer.ShowError($"Unknown plugin '{pluginName}'.");
            ConsoleRenderer.ShowInfo($"Available: {string.Join(", ", PluginRegistry.All.Keys)}");
            return 1;
        }

        var config = _configService.Load(configPath);

        if (config.GetPlugin(pluginName).IsEnabled)
        {
            ConsoleRenderer.ShowWarning($"Plugin '{pluginName}' is already enabled.");
            return 0;
        }

        // Build the plugin config entry from --set pairs, then mark it enabled.
        config.Plugins ??= new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var pluginValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in setOptions)
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0)
            {
                ConsoleRenderer.ShowError($"Invalid --set value '{pair}'. Expected key=value.");
                return 1;
            }
            pluginValues[pair[..idx].Trim()] = pair[(idx + 1)..].Trim();
        }
        pluginValues["enabled"] = "true";
        config.Plugins[pluginName] = pluginValues;

        var progress = new Progress<PhaseProgress>(p => ConsoleRenderer.ShowPhaseProgress(p));
        var context = new ProvisioningContext
        {
            Config = config,
            IsDryRun = dryRun,
            Progress = progress,
            CancellationToken = ct
        };

        // Skip every phase except connect-and-verify and this plugin's phases
        var skipPhases = _phases
            .Select(p => p.Name)
            .Where(name =>
                !name.Equals("connect-and-verify", StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith($"{pluginName}-", StringComparison.OrdinalIgnoreCase))
            .ToList();

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[bold]Adding plugin: {pluginName}[/]").LeftJustified());

        try
        {
            ValidatePluginCompatibility(metadata, config);

            if (!dryRun)
            {
                var server = config.Server;
                var expandedKey = server.SshKeyPath.Replace("~",
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                await _ssh.ConnectAsync(server.Host, server.SshUser, expandedKey, server.Port, ct);
                await ValidateRequiredPhasesAsync(metadata, config, ct);
            }

            await _orchestrator.RunAsync(context, skipPhases, ct);

            if (!dryRun)
                _configService.Save(config, configPath);

            ConsoleRenderer.ShowSuccess($"Plugin '{pluginName}' enabled and configured.");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]The connection string was written to the server's .env file.[/]");
            AnsiConsole.MarkupLine("[grey]Run [bold]gantry env list[/] to verify it is set.[/]");
            AnsiConsole.WriteLine();
            return 0;
        }
        catch (GantryException ex)
        {
            // Revert the in-memory change so config is not saved with a broken plugin entry
            config.Plugins.Remove(pluginName);
            ConsoleRenderer.ShowError(ex.Message);
            if (ex.Remediation != null) ConsoleRenderer.ShowInfo(ex.Remediation);
            return 1;
        }
        finally
        {
            await _ssh.DisposeAsync();
        }
    }

    public async Task<int> RemoveAsync(string pluginName, string configPath, bool dryRun, CancellationToken ct = default)
    {
        if (!PluginRegistry.All.ContainsKey(pluginName))
        {
            ConsoleRenderer.ShowError($"Unknown plugin '{pluginName}'.");
            ConsoleRenderer.ShowInfo($"Available: {string.Join(", ", PluginRegistry.All.Keys)}");
            return 1;
        }

        var config = _configService.Load(configPath);

        if (!config.GetPlugin(pluginName).IsEnabled)
        {
            ConsoleRenderer.ShowWarning($"Plugin '{pluginName}' is not enabled.");
            return 0;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[bold]Removing plugin: {pluginName}[/]").LeftJustified());

        if (!dryRun)
        {
            var server = config.Server;
            var expandedKey = server.SshKeyPath.Replace("~",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            await _ssh.ConnectAsync(server.Host, server.SshUser, expandedKey, server.Port, ct);
        }

        var context = new ProvisioningContext { Config = config, IsDryRun = dryRun, CancellationToken = ct };

        // Run rollbacks in reverse order for this plugin's phases only
        var pluginPhases = _phases
            .Where(p => p.Name.StartsWith($"{pluginName}-", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.Order)
            .ToList();

        foreach (var phase in pluginPhases)
        {
            try
            {
                await phase.RollbackAsync(context, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Rollback of phase '{Phase}' failed - continuing", phase.Name);
            }
        }

        config.Plugins?.Remove(pluginName);

        if (!dryRun)
            _configService.Save(config, configPath);

        ConsoleRenderer.ShowSuccess($"Plugin '{pluginName}' removed.");
        await _ssh.DisposeAsync();
        return 0;
    }

    internal static void ValidatePluginCompatibility(PluginMetadata metadata, DeployConfig config)
    {
        // OS version check (config.Server.OsVersion is populated by ConnectAndVerifyPhase)
        if (!string.IsNullOrEmpty(config.Server.OsVersion)
            && metadata.SupportedOsVersions.Count > 0
            && !metadata.SupportedOsVersions.Contains(config.Server.OsVersion))
        {
            throw new GantryException(
                $"Plugin '{metadata.Name}' does not support Ubuntu {config.Server.OsVersion}. " +
                $"Supported versions: {string.Join(", ", metadata.SupportedOsVersions)}.",
                "Upgrade your server to a supported Ubuntu version before installing this plugin.");
        }
    }

    private async Task ValidateRequiredPhasesAsync(PluginMetadata metadata, DeployConfig config, CancellationToken ct)
    {
        foreach (var phase in metadata.RequiredPhases)
        {
            string? command = phase switch
            {
                "os-hardening" => $"id {config.Server.DeployUser} 2>/dev/null",
                "runtime-installation" => "which dotnet 2>/dev/null",
                "process-manager-setup" => $"test -f /etc/systemd/system/{config.App.Name}.service",
                _ => null
            };

            if (command is null) continue;

            string missingMessage = phase switch
            {
                "os-hardening" => $"Deploy user '{config.Server.DeployUser}' does not exist on the server.",
                "runtime-installation" => "The .NET runtime is not installed on the server.",
                "process-manager-setup" => $"Systemd service for '{config.App.Name}' has not been created.",
                _ => string.Empty
            };

            var result = await _ssh.ExecuteAsync(command, ct: ct);
            if (!result.Success)
                throw new GantryException(
                    $"Plugin '{metadata.Name}' requires the '{phase}' provisioning phase to have completed. {missingMessage}",
                    "Run: gantry provision to prepare the server before installing this plugin.");
        }
    }
}
