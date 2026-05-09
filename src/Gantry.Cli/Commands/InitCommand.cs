using Gantry.Cli.UI;
using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Gantry.Core.Validation;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.CommandLine;

namespace Gantry.Cli.Commands;

public class InitCommand : Command
{
    public InitCommand() : base("init", "Full interactive server provisioning and CI/CD setup") { }
}

public class InitCommandHandler
{
    private readonly IPhaseOrchestrator _orchestrator;
    private readonly IConfigService _configService;
    private readonly DeployConfigValidator _validator;
    private readonly ILogger<InitCommandHandler> _logger;

    public InitCommandHandler(
        IPhaseOrchestrator orchestrator,
        IConfigService configService,
        DeployConfigValidator validator,
        ILogger<InitCommandHandler> logger)
    {
        _orchestrator = orchestrator;
        _configService = configService;
        _validator = validator;
        _logger = logger;
    }

    public async Task<int> ExecuteAsync(string configPath, bool dryRun, string[] skipPhases, CancellationToken ct = default)
    {
        ConsoleRenderer.ShowBanner();

        var defaults = _configService.Exists(configPath)
            ? _configService.Load(configPath)
            : _configService.CreateDefault();

        var config = Prompts.GatherConfig(defaults);

        var errors = _validator.Validate(config);
        if (errors.Count > 0)
        {
            ConsoleRenderer.ShowError("Configuration is invalid:");
            foreach (var error in errors)
                AnsiConsole.MarkupLine($"  [red]•[/] {Markup.Escape(error)}");
            return 1;
        }

        AnsiConsole.WriteLine();
        ConsoleRenderer.ShowInfo("Configuration summary:");
        ConsoleRenderer.ShowDeployConfig(config);

        if (!Prompts.AskBool("Proceed with provisioning?", true))
        {
            ConsoleRenderer.ShowInfo("Aborted.");
            return 0;
        }

        var progress = new Progress<PhaseProgress>(p => ConsoleRenderer.ShowPhaseProgress(p));

        var context = new ProvisioningContext
        {
            Config = config,
            IsDryRun = dryRun,
            Progress = progress,
            CancellationToken = ct
        };

        GenerateDeployKeyIfNeeded(context);

        try
        {
            _logger.LogInformation("Starting gantry init (dry-run={DryRun})", dryRun);
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[bold]Provisioning[/]"));

            await _orchestrator.RunAsync(context, skipPhases, ct);

            AnsiConsole.WriteLine();

            if (context.FailedOptionalPhases.Count > 0)
            {
                ConsoleRenderer.ShowWarning("Provisioning completed with warnings:");
                foreach (var (phase, reason) in context.FailedOptionalPhases)
                    AnsiConsole.MarkupLine($"  [yellow]![/] [grey]{Markup.Escape(phase)}:[/] {Markup.Escape(reason)}");
                AnsiConsole.WriteLine();
                if (context.FailedOptionalPhases.Any(f => f.Phase == "ssl-provisioning"))
                    ConsoleRenderer.ShowInfo("To complete SSL once DNS is propagated: gantry provision --phase ssl-provisioning");
                AnsiConsole.WriteLine();
            }
            else
            {
                ConsoleRenderer.ShowSuccess("Provisioning complete!");
            }

            ConsoleRenderer.ShowSummary("Next Steps", new[]
            {
                ("Domain", config.Domain.HasDomain ? $"https://{config.Domain.Name}" : $"http://{config.Server.Host}"),
                ("Workflow", context.GeneratedWorkflowPath ?? config.Ci.WorkflowPath),
                ("Config", configPath),
                ("Logs", Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".gantry", "logs"))
            });

            return 0;
        }
        catch (OperationCanceledException)
        {
            ConsoleRenderer.ShowWarning("Operation cancelled.");
            return 130;
        }
        catch (GantryException ex)
        {
            ConsoleRenderer.ShowError(ex.Message);
            if (ex.Remediation != null)
                ConsoleRenderer.ShowInfo($"Suggested fix: {ex.Remediation}");
            _logger.LogError(ex, "Provisioning failed");
            return 1;
        }
        catch (Exception ex)
        {
            ConsoleRenderer.ShowError($"Unexpected error: {ex.Message}");
            _logger.LogError(ex, "Unexpected error during init");
            return 1;
        }
    }

    private static void GenerateDeployKeyIfNeeded(ProvisioningContext context)
    {
        var deployKeyPath = context.Config.Server.DeployKeyPath
            .Replace("~", System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile));

        if (context.IsDryRun)
        {
            context.GeneratedDeployKeyPublic = "[dry-run-public-key]";
            return;
        }

        // Key already exists - still read the public key so os-hardening can install it
        // into authorized_keys (important when re-running init after a previous partial run)
        if (File.Exists(deployKeyPath))
        {
            var pubPath = deployKeyPath + ".pub";
            if (File.Exists(pubPath))
                context.GeneratedDeployKeyPublic = File.ReadAllText(pubPath).Trim();
            return;
        }

        AnsiConsole.Status().Start("Generating deploy SSH key...", _ =>
        {
            var dir = Path.GetDirectoryName(deployKeyPath)!;
            Directory.CreateDirectory(dir);
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ssh-keygen")
            {
                Arguments = $"-t ed25519 -f \"{deployKeyPath}\" -N \"\" -C \"gantry-deploy\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit();
        });

        if (File.Exists(deployKeyPath + ".pub"))
            context.GeneratedDeployKeyPublic = File.ReadAllText(deployKeyPath + ".pub").Trim();

        ConsoleRenderer.ShowSuccess($"Deploy key generated at {deployKeyPath}");
    }
}
