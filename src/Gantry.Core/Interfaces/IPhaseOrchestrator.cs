using Gantry.Core.Models;

namespace Gantry.Core.Interfaces;

/// <summary>Coordinates sequential phase execution with rollback support on failure.</summary>
public interface IPhaseOrchestrator
{
    Task RunAsync(ProvisioningContext context, IReadOnlyList<string>? skipPhases = null, CancellationToken ct = default);
}
