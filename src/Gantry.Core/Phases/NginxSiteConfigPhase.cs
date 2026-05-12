using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gantry.Core.Phases;

public class NginxSiteConfigPhase : PhaseBase
{
    private readonly ISshService _ssh;
    private readonly IWebServer _webServer;

    public NginxSiteConfigPhase(ISshService ssh, IWebServer webServer, ILogger<NginxSiteConfigPhase> logger)
        : base(logger)
    {
        _ssh = ssh;
        _webServer = webServer;
    }

    public override string Name        => "nginx-site-config";
    public override string Description => "Configure nginx reverse proxy for the application";
    public override int    Order       => 41;

    protected override async Task ValidatePrerequisitesAsync(ProvisioningContext context, CancellationToken ct)
    {
        if (context.IsDryRun) return;
        var result = await _ssh.ExecuteAsync("dpkg -s nginx &>/dev/null && echo ok || echo missing", ct: ct);
        if (result.Stdout.Trim() == "missing")
            throw new PhaseException(Name,
                "nginx is not installed.",
                "Run: gantry provision --phase nginx-install");
    }

    protected override async Task RunAsync(ProvisioningContext context, CancellationToken ct)
    {
        Report(context, PhaseStatus.Running, "Configuring nginx site...");
        if (!context.IsDryRun)
        {
            await _webServer.ConfigureAsync(_ssh, context.Config, false, ct);
            await _webServer.ReloadAsync(_ssh, false, ct);
        }
    }

    protected override async Task RunRollbackAsync(ProvisioningContext context, CancellationToken ct)
    {
        if (!context.IsDryRun)
            await _webServer.RollbackAsync(_ssh, context.Config, ct);
    }
}
