using Gantry.Core.Models;

namespace Gantry.Core.Interfaces;

/// <summary>Post-deploy lifecycle hook contributed by a plugin. Failure aborts the deploy.</summary>
public interface IDeployHook
{
    string PluginName { get; }
    Task RunAsync(ISshService ssh, DeployConfig config, CancellationToken ct = default);
}
