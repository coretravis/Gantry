using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gantry.Core.Phases;

public class WebServerConfigurationPhase : PhaseBase
{
    private readonly ISshService _ssh;
    private readonly IEnumerable<IWebServer> _webServers;

    public WebServerConfigurationPhase(ISshService ssh, IEnumerable<IWebServer> webServers, ILogger<WebServerConfigurationPhase> logger)
        : base(logger)
    {
        _ssh = ssh;
        _webServers = webServers;
    }

    public override string Name => "web-server-configuration";
    public override string Description => "Install and configure nginx as reverse proxy";
    public override int Order => 40;

    protected override async Task RunAsync(ProvisioningContext context, CancellationToken ct)
    {
        var serverType = context.Config.WebServer.Type;
        var webServer = _webServers.FirstOrDefault(w => w.ServerType.Equals(serverType, StringComparison.OrdinalIgnoreCase))
            ?? throw new PhaseException(Name, $"No web server implementation found for type '{serverType}'.");

        Report(context, PhaseStatus.Running, $"Installing {serverType}...");
        await webServer.InstallAsync(_ssh, context.IsDryRun, ct);

        Report(context, PhaseStatus.Running, $"Configuring {serverType} reverse proxy...");
        var configChanged = await webServer.ConfigureAsync(_ssh, context.Config, context.IsDryRun, ct);

        if (configChanged)
        {
            Report(context, PhaseStatus.Running, $"Reloading {serverType}...");
            await webServer.ReloadAsync(_ssh, context.IsDryRun, ct);
        }
        else
        {
            Logger.LogDebug("{ServerType} config unchanged, reload skipped", serverType);
        }
    }

    protected override async Task RunRollbackAsync(ProvisioningContext context, CancellationToken ct)
    {
        var serverType = context.Config.WebServer.Type;
        var webServer = _webServers.FirstOrDefault(w => w.ServerType.Equals(serverType, StringComparison.OrdinalIgnoreCase));
        if (webServer != null && !context.IsDryRun)
            await webServer.RollbackAsync(_ssh, context.Config, ct);
    }
}
