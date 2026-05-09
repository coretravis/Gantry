using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gantry.Infrastructure.SslProviders;

public class CertbotSslProvider : ISslProvider
{
    private readonly ILogger<CertbotSslProvider> _logger;

    public CertbotSslProvider(ILogger<CertbotSslProvider> logger) => _logger = logger;

    public async Task InstallAsync(ISshService ssh, bool isDryRun, CancellationToken ct = default)
    {
        _logger.LogInformation("Installing certbot and nginx plugin");
        if (!isDryRun)
        {
            var result = await ssh.ExecuteAsync("apt-get install -y certbot python3-certbot-nginx", TimeSpan.FromMinutes(3), ct);
            if (!result.Success)
                throw new PhaseException("ssl-provisioning", $"Certbot installation failed: {result.Stderr}");
        }
    }

    public async Task ObtainCertificateAsync(ISshService ssh, DomainConfig domain, bool isDryRun, CancellationToken ct = default)
    {
        var domainArgs = $"-d {domain.Name}";
        if (domain.Www) domainArgs += $" -d www.{domain.Name}";

        var command = $"certbot --nginx {domainArgs} --non-interactive --agree-tos -m {domain.SslEmail} --redirect";
        _logger.LogInformation("Requesting certificate for {Domain}", domain.Name);

        if (!isDryRun)
        {
            var result = await ssh.ExecuteAsync(command, TimeSpan.FromMinutes(3), ct);
            if (!result.Success)
                throw new PhaseException("ssl-provisioning",
                    $"Certbot certificate issuance failed for {domain.Name}: {result.Stderr}",
                    "Verify DNS is pointing to this server and port 80 is accessible.");

            _logger.LogInformation("Certificate obtained for {Domain}", domain.Name);
        }
    }

    public async Task<bool> VerifyRenewalAsync(ISshService ssh, CancellationToken ct = default)
    {
        _logger.LogDebug("Running certbot renewal dry-run");
        var result = await ssh.ExecuteAsync("certbot renew --dry-run", TimeSpan.FromMinutes(5), ct);
        return result.Success &&
               (result.Stdout.Contains("simulated renewals succeeded", StringComparison.OrdinalIgnoreCase) ||
                result.Stderr.Contains("simulated renewals succeeded", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<DateTimeOffset?> GetExpiryAsync(ISshService ssh, string domain, CancellationToken ct = default)
    {
        var result = await ssh.ExecuteAsync($"certbot certificates -d {domain} 2>/dev/null | grep 'Expiry Date'", ct: ct);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Stdout)) return null;

        var line = result.Stdout.Trim();
        var datePart = line.Split(':').Last().Trim().Split(' ').First();
        return DateTimeOffset.TryParse(datePart, out var expiry) ? expiry : null;
    }

    public async Task RollbackAsync(ISshService ssh, string domain, CancellationToken ct = default)
    {
        _logger.LogInformation("Revoking and deleting certificate for {Domain}", domain);
        await ssh.ExecuteAsync($"certbot delete --cert-name {domain} --non-interactive 2>/dev/null || true", ct: ct);
    }
}
