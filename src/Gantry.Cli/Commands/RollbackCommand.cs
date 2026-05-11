using Gantry.Cli.UI;
using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.CommandLine;

namespace Gantry.Cli.Commands;

public class RollbackCommand : Command
{
    public RollbackCommand() : base("rollback", "Roll back to a previous release") { }
}

public class RollbackCommandHandler
{
    private readonly ISshService _ssh;
    private readonly IProcessManager _processManager;
    private readonly IConfigService _configService;
    private readonly IStateService _stateService;
    private readonly ILogger<RollbackCommandHandler> _logger;

    public RollbackCommandHandler(
        ISshService ssh,
        IProcessManager processManager,
        IConfigService configService,
        IStateService stateService,
        ILogger<RollbackCommandHandler> logger)
    {
        _ssh = ssh;
        _processManager = processManager;
        _configService = configService;
        _stateService = stateService;
        _logger = logger;
    }

    public async Task<int> ExecuteAsync(string configPath, string? releaseId, CancellationToken ct = default)
    {
        var config = _configService.Load(configPath);
        var server = config.Server;
        var expandedKey = server.DeployKeyPath.Replace("~", System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile));

        try
        {
            await _ssh.ConnectAsync(server.Host, server.DeployUser, expandedKey, server.Port, ct);

            var state = await _stateService.ReadAsync(_ssh, config.App.Name, ct);
            var currentRelease = state.CurrentRelease;

            var releasesDir = $"/var/www/{config.App.Name}/releases";
            var releasesResult = await _ssh.ExecuteAsync($"ls -1dt {releasesDir}/*/ 2>/dev/null | head -10", ct: ct);

            if (!releasesResult.Success || string.IsNullOrWhiteSpace(releasesResult.Stdout))
            {
                ConsoleRenderer.ShowError("No rollback releases found.");
                return 1;
            }

            var availableReleases = releasesResult.Stdout
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.TrimEnd('/').Split('/').Last())
                .ToList();

            var choices = availableReleases
                .Select(r => r == currentRelease ? $"{r} (current)" : r)
                .ToList();

            var selectedChoice = releaseId ?? AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select release to roll back to:")
                    .AddChoices(choices));

            var selectedRelease = selectedChoice.EndsWith(" (current)")
                ? selectedChoice[..^" (current)".Length]
                : selectedChoice;

            if (!string.IsNullOrEmpty(currentRelease) && selectedRelease == currentRelease)
            {
                ConsoleRenderer.ShowWarning($"Release {currentRelease} is already the active release.");
                return 0;
            }

            var releasePath = $"{releasesDir}/{selectedRelease}";
            var deployPath = string.IsNullOrWhiteSpace(config.App.DeployPath)
                ? $"/var/www/{config.App.Name}/app"
                : config.App.DeployPath;

            ConsoleRenderer.ShowInfo($"Rolling back to release {selectedRelease}...");

            await _processManager.StopAsync(_ssh, config.App.Name, ct);
            await _ssh.ExecuteAsync($"cp -r {releasePath}/. {deployPath}/", ct: ct);
            await _processManager.StartAsync(_ssh, config.App.Name, ct);

            await Task.Delay(3000, ct);
            var isActive = await _processManager.IsActiveAsync(_ssh, config.App.Name, ct);
            if (!isActive)
                throw new GantryException($"Service did not start after rollback to {selectedRelease}.",
                    "Check logs: gantry status");

            ConsoleRenderer.ShowSuccess($"Rolled back to {selectedRelease} successfully.");
            return 0;
        }
        finally
        {
            await _ssh.DisposeAsync();
        }
    }
}
