using FluentAssertions;
using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Gantry.Core.Phases;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Gantry.Core.Tests.Phases;

public class NginxSiteConfigPhaseTests
{
    private readonly Mock<ISshService> _ssh = new();
    private readonly Mock<IWebServer> _webServer = new();
    private readonly NginxSiteConfigPhase _sut;

    public NginxSiteConfigPhaseTests()
    {
        _sut = new NginxSiteConfigPhase(_ssh.Object, _webServer.Object, NullLogger<NginxSiteConfigPhase>.Instance);

        _ssh.Setup(s => s.ExecuteAsync(
                It.Is<string>(c => c.Contains("dpkg -s nginx")),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 0, Stdout = "ok" });

        _webServer.Setup(w => w.ConfigureAsync(
                It.IsAny<ISshService>(), It.IsAny<DeployConfig>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
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
    public async Task NginxSiteConfig_WhenNotDryRun_ConfiguresAndReloads()
    {
        await _sut.ExecuteAsync(BuildContext());

        _webServer.Verify(w => w.ConfigureAsync(_ssh.Object, It.IsAny<DeployConfig>(), false, It.IsAny<CancellationToken>()), Times.Once);
        _webServer.Verify(w => w.ReloadAsync(_ssh.Object, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NginxSiteConfig_WhenNginxMissing_ThrowsWithRemediation()
    {
        _ssh.Setup(s => s.ExecuteAsync(
                It.Is<string>(c => c.Contains("dpkg -s nginx")),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 0, Stdout = "missing" });

        var act = async () => await _sut.ExecuteAsync(BuildContext());

        await act.Should().ThrowAsync<PhaseException>()
            .WithMessage("*nginx is not installed*");
    }

    [Fact]
    public async Task NginxSiteConfig_Rollback_CallsWebServerRollback()
    {
        await _sut.RollbackAsync(BuildContext());

        _webServer.Verify(w => w.RollbackAsync(_ssh.Object, It.IsAny<DeployConfig>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
