using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gantry.Core.Phases;

public class ConfigPersistencePhase : PhaseBase
{
    private readonly IConfigService _configService;

    public ConfigPersistencePhase(IConfigService configService, ILogger<ConfigPersistencePhase> logger)
        : base(logger) => _configService = configService;

    public override string Name => "config-persistence";
    public override string Description => "Save deployment configuration to .deploy.yml";
    public override int Order => 80;

    protected override Task RunAsync(ProvisioningContext context, CancellationToken ct)
    {
        var configPath = Path.Combine(Directory.GetCurrentDirectory(), ".deploy.yml");

        Report(context, PhaseStatus.Running, $"Saving configuration to {configPath}...");

        if (!context.IsDryRun)
        {
            _configService.Save(context.Config, configPath);
            Logger.LogInformation("Configuration saved to {Path}", configPath);

            EnsureGitIgnore(configPath);
        }
        else
        {
            Logger.LogDebug("[dry-run] Would save configuration to {Path}", configPath);
        }

        return Task.CompletedTask;
    }

    private void EnsureGitIgnore(string configPath)
    {
        var gitIgnorePath = Path.Combine(Directory.GetCurrentDirectory(), ".gitignore");
        if (!File.Exists(gitIgnorePath))
        {
            Logger.LogWarning(".gitignore not found. Consider adding .deploy.yml to .gitignore.");
            return;
        }

        var content = File.ReadAllText(gitIgnorePath);
        if (!content.Contains(".deploy.yml"))
        {
            Logger.LogWarning(".deploy.yml is not in .gitignore. It may be accidentally committed. Add it manually if it contains sensitive paths.");
        }
    }

    protected override Task RunRollbackAsync(ProvisioningContext context, CancellationToken ct)
    {
        var configPath = Path.Combine(Directory.GetCurrentDirectory(), ".deploy.yml");
        if (File.Exists(configPath) && !context.IsDryRun)
        {
            File.Delete(configPath);
            Logger.LogInformation("Removed .deploy.yml as part of rollback");
        }
        return Task.CompletedTask;
    }
}
