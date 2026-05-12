using FluentAssertions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Gantry.Core.Phases;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Gantry.Core.Tests.Phases;

public class NginxInstallPhaseTests
{
    private readonly Mock<ISshService> _ssh = new();
    private readonly Mock<IWebServer> _webServer = new();
    private readonly NginxInstallPhase _sut;

    public NginxInstallPhaseTests()
    {
        _sut = new NginxInstallPhase(_ssh.Object, _webServer.Object, NullLogger<NginxInstallPhase>.Instance);
    }

    private static ProvisioningContext BuildContext(bool dryRun = false) => new()
    {
        Config = new DeployConfig
        {
            App = new AppConfig { Name = "test-app" },
            Server = new ServerConfig()
        },
        IsDryRun = dryRun
    };

    [Fact]
    public async Task NginxInstall_WhenNotDryRun_CallsInstall()
    {
        await _sut.ExecuteAsync(BuildContext());

        _webServer.Verify(w => w.InstallAsync(_ssh.Object, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NginxInstall_WhenDryRun_SkipsInstall()
    {
        await _sut.ExecuteAsync(BuildContext(dryRun: true));

        _webServer.Verify(w => w.InstallAsync(It.IsAny<ISshService>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
