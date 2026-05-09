using Gantry.Core.Models;

namespace Gantry.Core.Interfaces;

/// <summary>Installs and configures a web server as a reverse proxy.</summary>
public interface IWebServer
{
    string ServerType { get; }
    Task InstallAsync(ISshService ssh, bool isDryRun, CancellationToken ct = default);
    Task<bool> ConfigureAsync(ISshService ssh, DeployConfig config, bool isDryRun, CancellationToken ct = default);
    Task ReloadAsync(ISshService ssh, bool isDryRun, CancellationToken ct = default);
    Task RollbackAsync(ISshService ssh, DeployConfig config, CancellationToken ct = default);
}
