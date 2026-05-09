using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gantry.Core.Phases;

/// <summary>
/// Base class for plugin provisioning phases. Automatically skips execution when the plugin
/// is not enabled in .deploy.yml. Plugins set IsRequired = false so the pipeline continues
/// on failure rather than rolling back the entire provisioning run.
/// </summary>
public abstract class PluginPhaseBase : PhaseBase
{
    protected readonly ISshService _ssh;

    protected PluginPhaseBase(ISshService ssh, ILogger logger) : base(logger)
    {
        _ssh = ssh;
    }

    public abstract string PluginName { get; }

    public override bool IsRequired => false;

    public override async Task ExecuteAsync(ProvisioningContext context, CancellationToken ct = default)
    {
        var pluginConfig = context.Config.GetPlugin(PluginName);
        if (!pluginConfig.IsEnabled)
        {
            Logger.LogDebug("Plugin '{Plugin}' is not enabled - skipping phase '{Phase}'", PluginName, Name);
            Report(context, PhaseStatus.Skipped, $"{Name} skipped - plugin not enabled");
            return;
        }

        await base.ExecuteAsync(context, ct);
    }
}
