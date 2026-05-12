using Gantry.Cli.UI;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.CommandLine;

namespace Gantry.Cli.Commands;

public class TeardownCommand : Command
{
    public TeardownCommand() : base("teardown", "Remove all server configuration created by gantry init") { }
}

public class TeardownCommandHandler
{
    private readonly ISshService _ssh;
    private readonly IConfigService _configService;
    private readonly IEnumerable<IPhase> _phases;
    private readonly ILogger<TeardownCommandHandler> _logger;
    private readonly Func<string, bool> _confirm;

    public TeardownCommandHandler(
        ISshService ssh,
        IConfigService configService,
        IEnumerable<IPhase> phases,
        ILogger<TeardownCommandHandler> logger,
        Func<string, bool>? confirm = null)
    {
        _ssh = ssh;
        _configService = configService;
        _phases = phases;
        _logger = logger;
        _confirm = confirm ?? Confirm;
    }

    public async Task<int> ExecuteAsync(string configPath, bool dryRun, CancellationToken ct = default)
    {
        var config = _configService.Load(configPath);

        ConsoleRenderer.ShowWarning(
            $"This will remove all Gantry-managed configuration for '{config.App.Name}' " +
            $"from {config.Server.Host}. This cannot be undone.");

        if (!dryRun && !_confirm(config.App.Name))
            return 0;

        var server = config.Server;
        var expandedKey = server.SshKeyPath.Replace("~",
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        if (!dryRun)
            await _ssh.ConnectAsync(server.Host, server.SshUser, expandedKey, server.Port, ct);

        try
        {
            var context = new ProvisioningContext
            {
                Config = config,
                IsDryRun = dryRun,
            };

            var teardownPhases = _phases
                .Where(p => p.Name is not ("ci-generation" or "config-persistence" or "connect-and-verify"))
                .OrderByDescending(p => p.Order)
                .ToList();

            foreach (var phase in teardownPhases)
            {
                if (dryRun)
                {
                    _logger.LogDebug("[dry-run] Would remove: {Phase}", phase.Name);
                    continue;
                }

                try
                {
                    Report(phase.Name, "Removing...");
                    await phase.RollbackAsync(context, ct);
                    Report(phase.Name, "Done");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Teardown step '{Phase}' failed — continuing", phase.Name);
                }
            }

            ConsoleRenderer.ShowSuccess($"Teardown complete for '{config.App.Name}'.");
            ShowReminders(config);
            return 0;
        }
        finally
        {
            await _ssh.DisposeAsync();
        }
    }

    private void ShowReminders(DeployConfig config)
    {
        _logger.LogInformation(
            "Manual steps remaining: delete GitHub secrets DO_HOST/DO_SSH_KEY, delete deploy key {DeployKeyPath}, remove .deploy.yml if no longer needed",
            config.Server.DeployKeyPath);
        ConsoleRenderer.ShowInfo("Manual steps remaining:");
        ConsoleRenderer.ShowInfo("   Delete GitHub secrets: DO_HOST, DO_SSH_KEY in your repository settings");
        ConsoleRenderer.ShowInfo($"   Delete local deploy key: {config.Server.DeployKeyPath}");
        ConsoleRenderer.ShowInfo("   Remove .deploy.yml if no longer needed");
    }

    private static void Report(string phase, string message) =>
        ConsoleRenderer.ShowInfo($"{phase}: {message}");

    private static bool Confirm(string appName)
    {
        var entered = AnsiConsole.Prompt(
            new TextPrompt<string>("Type the app name to confirm:"));
        return entered == appName;
    }
}
