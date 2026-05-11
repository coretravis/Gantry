using Gantry.Core.Models;

namespace Gantry.Core.Interfaces;

/// <summary>Reads and writes the server-side gantry.json state file.</summary>
public interface IStateService
{
    Task<GantryState> ReadAsync(ISshService ssh, string appName, CancellationToken ct = default);
    Task WriteAsync(ISshService ssh, string appName, GantryState state, CancellationToken ct = default);
}
