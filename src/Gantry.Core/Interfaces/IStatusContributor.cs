using Gantry.Core.Models;

namespace Gantry.Core.Interfaces;

/// <summary>Read-only health check contributed by a plugin to <c>gantry status</c>.</summary>
public interface IStatusContributor
{
    string PluginName { get; }
    Task<IReadOnlyList<HealthCheck>> GetHealthAsync(ISshService ssh, DeployConfig config, CancellationToken ct = default);
}
