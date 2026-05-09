using Gantry.Core.Models;

namespace Gantry.Core.Interfaces;

/// <summary>Provisions and renews SSL certificates on the target server.</summary>
public interface ISslProvider
{
    Task InstallAsync(ISshService ssh, bool isDryRun, CancellationToken ct = default);
    Task ObtainCertificateAsync(ISshService ssh, DomainConfig domain, bool isDryRun, CancellationToken ct = default);
    Task<bool> VerifyRenewalAsync(ISshService ssh, CancellationToken ct = default);
    Task<DateTimeOffset?> GetExpiryAsync(ISshService ssh, string domain, CancellationToken ct = default);
    Task RollbackAsync(ISshService ssh, string domain, CancellationToken ct = default);
}
