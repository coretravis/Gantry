using Gantry.Core.Models;

namespace Gantry.Core.Interfaces;

/// <summary>Creates and manages application process lifecycle via a process supervisor.</summary>
public interface IProcessManager
{
    Task CreateServiceAsync(ISshService ssh, DeployConfig config, bool isDryRun, CancellationToken ct = default);
    Task StartAsync(ISshService ssh, string serviceName, CancellationToken ct = default);
    Task StopAsync(ISshService ssh, string serviceName, CancellationToken ct = default);
    Task RestartAsync(ISshService ssh, string serviceName, CancellationToken ct = default);
    Task<bool> IsActiveAsync(ISshService ssh, string serviceName, CancellationToken ct = default);
    Task<string> GetStatusAsync(ISshService ssh, string serviceName, CancellationToken ct = default);
    Task<string> GetLogsAsync(ISshService ssh, string serviceName, int lines, CancellationToken ct = default);
    Task RollbackAsync(ISshService ssh, DeployConfig config, CancellationToken ct = default);
}
