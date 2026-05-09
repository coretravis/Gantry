namespace Gantry.Core.Interfaces;

/// <summary>Manages GitHub secrets and repository operations via the gh CLI.</summary>
public interface IGithubService
{
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    Task SetSecretAsync(string name, string value, CancellationToken ct = default);
    Task<bool> SecretExistsAsync(string name, CancellationToken ct = default);
}
