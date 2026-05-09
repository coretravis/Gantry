using Gantry.Core.Models;

namespace Gantry.Core.Interfaces;

/// <summary>Installs and verifies an application runtime on the target server.</summary>
public interface IRuntimeProvider
{
    string Ecosystem { get; }
    IReadOnlyList<string> SupportedVersions { get; }
    Task InstallAsync(ISshService ssh, string version, bool isDryRun, CancellationToken ct = default);
    Task<bool> IsInstalledAsync(ISshService ssh, string version, CancellationToken ct = default);
    Task<string> GetExecutablePathAsync(ISshService ssh, CancellationToken ct = default);
    Task RollbackAsync(ISshService ssh, string version, CancellationToken ct = default);
}
