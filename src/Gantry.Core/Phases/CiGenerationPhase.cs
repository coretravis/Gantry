using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gantry.Core.Phases;

public class CiGenerationPhase : PhaseBase
{
    private readonly IEnumerable<ICiGenerator> _generators;
    private readonly IGithubService _github;

    public CiGenerationPhase(IEnumerable<ICiGenerator> generators, IGithubService github, ILogger<CiGenerationPhase> logger)
        : base(logger)
    {
        _generators = generators;
        _github = github;
    }

    public override string Name => "ci-generation";
    public override string Description => "Generate CI/CD workflow and configure secrets";
    public override int Order => 70;

    protected override async Task RunAsync(ProvisioningContext context, CancellationToken ct)
    {
        var ciConfig = context.Config.Ci;
        var generator = _generators.FirstOrDefault(g => g.Platform.Equals(ciConfig.Platform, StringComparison.OrdinalIgnoreCase))
            ?? throw new PhaseException(Name, $"No CI generator found for platform '{ciConfig.Platform}'.",
                $"Supported platforms: {string.Join(", ", _generators.Select(g => g.Platform))}");

        Report(context, PhaseStatus.Running, $"Generating {ciConfig.Platform} workflow...");
        var workflow = generator.GenerateWorkflow(context.Config);
        var workflowPath = generator.GetWorkflowPath(context.Config);
        var fullPath = Path.Combine(Directory.GetCurrentDirectory(), workflowPath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        if (File.Exists(fullPath) && !context.IsDryRun)
            Logger.LogWarning("Workflow file already exists at {Path}, overwriting.", workflowPath);

        if (!context.IsDryRun)
        {
            await File.WriteAllTextAsync(fullPath, workflow, ct);
            Logger.LogInformation("Workflow written to {Path}", workflowPath);
        }
        else
        {
            Logger.LogDebug("[dry-run] Would write workflow to {Path}", workflowPath);
        }

        context.GeneratedWorkflowPath = workflowPath;

        Report(context, PhaseStatus.Running, "Configuring GitHub secrets...");
        await SetSecretsAsync(context, ct);
    }

    private async Task SetSecretsAsync(ProvisioningContext context, CancellationToken ct)
    {
        var ghAvailable = await _github.IsAvailableAsync(ct);
        if (!ghAvailable)
        {
            Logger.LogWarning("gh CLI not found. Set the following GitHub Actions secrets manually:");
            Logger.LogWarning("  DO_HOST = {Host}", context.Config.Server.Host);
            Logger.LogWarning("  DO_USER = {User}", context.Config.Server.DeployUser);
            Logger.LogWarning("  DO_SSH_KEY = <contents of {KeyPath}>", context.Config.Server.DeployKeyPath);
            return;
        }

        if (context.IsDryRun)
        {
            Logger.LogDebug("[dry-run] Would set GitHub secrets: DO_HOST, DO_USER, DO_SSH_KEY");
            return;
        }

        await _github.SetSecretAsync("DO_HOST", context.Config.Server.Host, ct);
        await _github.SetSecretAsync("DO_USER", context.Config.Server.DeployUser, ct);

        var deployKeyPath = context.Config.Server.DeployKeyPath.Replace("~",
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile));
        if (File.Exists(deployKeyPath))
        {
            var keyContent = await File.ReadAllTextAsync(deployKeyPath, ct);
            await _github.SetSecretAsync("DO_SSH_KEY", keyContent, ct);
            Logger.LogInformation("GitHub secrets DO_HOST, DO_USER, DO_SSH_KEY set successfully");
        }
        else
        {
            Logger.LogWarning("Deploy key not found at {Path}. Set DO_SSH_KEY secret manually.", deployKeyPath);
        }
    }

    protected override Task RunRollbackAsync(ProvisioningContext context, CancellationToken ct)
    {
        if (context.GeneratedWorkflowPath != null)
        {
            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), context.GeneratedWorkflowPath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                Logger.LogInformation("Removed generated workflow file {Path}", context.GeneratedWorkflowPath);
            }
        }
        return Task.CompletedTask;
    }
}
