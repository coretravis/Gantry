using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gantry.Core.Phases;

public class SslProvisioningPhase : PhaseBase
{
    private readonly ISshService _ssh;
    private readonly ISslProvider _sslProvider;

    public SslProvisioningPhase(ISshService ssh, ISslProvider sslProvider, ILogger<SslProvisioningPhase> logger)
        : base(logger)
    {
        _ssh = ssh;
        _sslProvider = sslProvider;
    }

    public override string Name => "ssl-provisioning";
    public override string Description => "Obtain Let's Encrypt SSL certificate";
    public override int Order => 60;
    public override bool IsRequired => false;

    protected override async Task RunAsync(ProvisioningContext context, CancellationToken ct)
    {
        if (!context.Config.Domain.HasDomain || !context.Config.Domain.Ssl)
        {
            Logger.LogInformation("No domain configured or SSL disabled, skipping SSL provisioning");
            Report(context, PhaseStatus.Skipped, "SSL skipped - no domain configured.");
            return;
        }

        var domain = context.Config.Domain;

        Report(context, PhaseStatus.Running, $"Verifying DNS for {domain.Name}...");
        if (!context.IsDryRun)
        {
            try
            {
                var addresses = await System.Net.Dns.GetHostAddressesAsync(domain.Name, ct);
                var serverHost = context.Config.Server.Host;
                if (!addresses.Any(a => a.ToString() == serverHost))
                    Logger.LogWarning("DNS for {Domain} does not resolve to {Server}. Certificate issuance may fail.", domain.Name, serverHost);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("DNS lookup for {Domain} failed: {Error}. Proceeding anyway.", domain.Name, ex.Message);
            }
        }

        // Check if a valid certificate already exists (with > 30 days remaining)
        var certAlreadyValid = false;
        if (!context.IsDryRun)
        {
            var existingExpiry = await _sslProvider.GetExpiryAsync(_ssh, domain.Name, ct);
            if (existingExpiry.HasValue && existingExpiry.Value > DateTimeOffset.UtcNow.AddDays(30))
            {
                certAlreadyValid = true;
                Logger.LogInformation(
                    "Valid certificate for {Domain} already exists (expires {Expiry:yyyy-MM-dd}), skipping issuance",
                    domain.Name, existingExpiry.Value);
                Report(context, PhaseStatus.Running,
                    $"Certificate for {domain.Name} valid until {existingExpiry.Value:yyyy-MM-dd}, skipping.");
            }
        }

        if (!certAlreadyValid)
        {
            Report(context, PhaseStatus.Running, "Installing Certbot...");
            await _sslProvider.InstallAsync(_ssh, context.IsDryRun, ct);

            Report(context, PhaseStatus.Running, $"Obtaining certificate for {domain.Name}...");
            await _sslProvider.ObtainCertificateAsync(_ssh, domain, context.IsDryRun, ct);
        }

        Report(context, PhaseStatus.Running, "Verifying auto-renewal configuration...");
        if (!context.IsDryRun)
        {
            try
            {
                var renewalOk = await _sslProvider.VerifyRenewalAsync(_ssh, ct);
                if (!renewalOk)
                    Logger.LogWarning("Certbot dry-run renewal check returned a non-success result. Auto-renewal may not be working. Investigate with: certbot renew --dry-run");
                else
                    Logger.LogInformation("Auto-renewal dry-run passed successfully");
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Auto-renewal dry-run check could not complete ({Error}). The certificate is valid - this does not affect SSL.", ex.Message);
            }

            var expiry = await _sslProvider.GetExpiryAsync(_ssh, domain.Name, ct);
            if (expiry.HasValue)
                Logger.LogInformation("Certificate for {Domain} expires on {Expiry:yyyy-MM-dd}", domain.Name, expiry.Value);
        }
    }

    protected override async Task RunRollbackAsync(ProvisioningContext context, CancellationToken ct)
    {
        if (context.Config.Domain.HasDomain && !context.IsDryRun)
            await _sslProvider.RollbackAsync(_ssh, context.Config.Domain.Name, ct);
    }
}
