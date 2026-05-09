using Gantry.Cli.UI;
using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.CommandLine;

namespace Gantry.Cli.Commands;

public class ProvisionCommand : Command
{
    public ProvisionCommand() : base("provision", "Re-run server provisioning") { }
}

public class ProvisionCommandHandler
{
    private readonly IPhaseOrchestrator _orchestrator;
    private readonly IEnumerable<IPhase> _phases;
    private readonly IConfigService _configService;
    private readonly ILogger<ProvisionCommandHandler> _logger;

    public ProvisionCommandHandler(
        IPhaseOrchestrator orchestrator,
        IEnumerable<IPhase> phases,
        IConfigService configService,
        ILogger<ProvisionCommandHandler> logger)
    {
        _orchestrator = orchestrator;
        _phases = phases;
        _configService = configService;
        _logger = logger;
    }

    public async Task<int> ExecuteAsync(string configPath, bool dryRun, string? singlePhase, string[] skipPhases, CancellationToken ct = default)
    {
        var config = _configService.Load(configPath);
        var progress = new Progress<PhaseProgress>(p => ConsoleRenderer.ShowPhaseProgress(p));
        var context = new ProvisioningContext { Config = config, IsDryRun = dryRun, Progress = progress, CancellationToken = ct };

        // Load the deploy public key into context so os-hardening can install it into authorized_keys
        var deployKeyPub = config.Server.DeployKeyPath
            .Replace("~", System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile)) + ".pub";
        if (File.Exists(deployKeyPub))
            context.GeneratedDeployKeyPublic = File.ReadAllText(deployKeyPub).Trim();

        // Phases that should never run in the provision command
        var neverRun = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "ci-generation", "config-persistence" };

        IReadOnlyList<string> skip;
        if (singlePhase != null)
        {
            // Build skip list dynamically from all registered phases so plugin phases
            // are automatically included/excluded without hardcoding names here.
            skip = _phases
                .Select(p => p.Name)
                .Where(name =>
                    !name.Equals("connect-and-verify", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals(singlePhase, StringComparison.OrdinalIgnoreCase))
                .Concat(neverRun)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        else
        {
            skip = skipPhases.Concat(neverRun).ToArray();
        }

        try
        {
            AnsiConsole.Write(new Rule("[bold]Provisioning[/]"));
            await _orchestrator.RunAsync(context, skip, ct);

            if (context.FailedOptionalPhases.Count > 0)
            {
                ConsoleRenderer.ShowWarning("Provisioning completed with warnings:");
                foreach (var (phase, reason) in context.FailedOptionalPhases)
                    AnsiConsole.MarkupLine($"  [yellow]![/] [grey]{Markup.Escape(phase)}:[/] {Markup.Escape(reason)}");
                if (context.FailedOptionalPhases.Any(f => f.Phase == "ssl-provisioning"))
                    ConsoleRenderer.ShowInfo("To complete SSL once DNS is propagated: gantry provision --phase ssl-provisioning");
            }
            else
            {
                ConsoleRenderer.ShowSuccess("Provisioning complete.");
            }

            return 0;
        }
        catch (GantryException ex)
        {
            ConsoleRenderer.ShowError(ex.Message);
            if (ex.Remediation != null) ConsoleRenderer.ShowInfo(ex.Remediation);
            return 1;
        }
    }
}
