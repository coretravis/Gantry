using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gantry.Core.Phases;

public class NginxInstallPhase : PhaseBase
{
    private readonly ISshService _ssh;
    private readonly IWebServer _webServer;

    public NginxInstallPhase(ISshService ssh, IWebServer webServer, ILogger<NginxInstallPhase> logger)
        : base(logger)
    {
        _ssh = ssh;
        _webServer = webServer;
    }

    public override string Name        => "nginx-install";
    public override string Description => "Install nginx web server";
    public override int    Order       => 40;

    protected override async Task RunAsync(ProvisioningContext context, CancellationToken ct)
    {
        Report(context, PhaseStatus.Running, "Installing nginx...");
        if (!context.IsDryRun)
            await _webServer.InstallAsync(_ssh, false, ct);
    }

    protected override async Task RunRollbackAsync(ProvisioningContext context, CancellationToken ct)
    {
        if (!context.IsDryRun)
            await _ssh.ExecuteAsync("apt-get remove -y nginx 2>/dev/null || true", ct: ct);
    }
}
