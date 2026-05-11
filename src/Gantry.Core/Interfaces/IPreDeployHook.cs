using Gantry.Core.Models;

namespace Gantry.Core.Interfaces;

/// <summary>
/// Pre-deploy lifecycle hook contributed by a plugin. Runs after the new build is extracted
/// but before the service is restarted. Failure aborts the deploy and leaves the previous
/// release active. Use for schema migrations and other pre-activation steps.
/// </summary>
public interface IPreDeployHook
{
    string PluginName { get; }
    Task RunAsync(ISshService ssh, DeployConfig config, CancellationToken ct = default);
}
