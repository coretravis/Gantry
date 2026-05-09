using Gantry.Cli.UI;
using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.CommandLine;

namespace Gantry.Cli.Commands;

public class CiCommand : Command
{
    public CiCommand() : base("ci", "Regenerate CI/CD workflow and secrets") { }
}

public class CiCommandHandler
{
    private readonly IPhaseOrchestrator _orchestrator;
    private readonly IConfigService _configService;
    private readonly ILogger<CiCommandHandler> _logger;

    public CiCommandHandler(IPhaseOrchestrator orchestrator, IConfigService configService, ILogger<CiCommandHandler> logger)
    {
        _orchestrator = orchestrator;
        _configService = configService;
        _logger = logger;
    }

    public async Task<int> ExecuteAsync(string configPath, bool dryRun, CancellationToken ct = default)
    {
        var config = _configService.Load(configPath);
        var progress = new Progress<PhaseProgress>(p => ConsoleRenderer.ShowPhaseProgress(p));
        var context = new ProvisioningContext { Config = config, IsDryRun = dryRun, Progress = progress, CancellationToken = ct };

        var skipAll = new[] { "connect-and-verify", "os-hardening", "runtime-installation", "web-server-configuration", "process-manager-setup", "ssl-provisioning", "config-persistence" };

        try
        {
            AnsiConsole.Write(new Rule("[bold]CI Generation[/]"));
            await _orchestrator.RunAsync(context, skipAll, ct);
            ConsoleRenderer.ShowSuccess("CI workflow generated.");
            if (context.GeneratedWorkflowPath != null)
                ConsoleRenderer.ShowInfo($"Written to: {context.GeneratedWorkflowPath}");
            return 0;
        }
        catch (GantryException ex)
        {
            ConsoleRenderer.ShowError(ex.Message);
            return 1;
        }
    }
}
