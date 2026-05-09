using Gantry.Core.Models;

namespace Gantry.Core.Interfaces;

/// <summary>Represents a discrete, ordered, rollback-capable provisioning step.</summary>
public interface IPhase
{
    string Name { get; }
    string Description { get; }
    int Order { get; }
    bool IsRequired { get; }

    Task ExecuteAsync(ProvisioningContext context, CancellationToken ct = default);
    Task RollbackAsync(ProvisioningContext context, CancellationToken ct = default);
}
