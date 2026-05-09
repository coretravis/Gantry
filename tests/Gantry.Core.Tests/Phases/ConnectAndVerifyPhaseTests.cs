using FluentAssertions;
using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Gantry.Core.Phases;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Gantry.Core.Tests.Phases;

public class ConnectAndVerifyPhaseTests
{
    private static ProvisioningContext BuildContext(bool dryRun = true) => new()
    {
        Config = new DeployConfig
        {
            Server = new ServerConfig
            {
                Host = "192.168.1.1",
                SshUser = "root",
                SshKeyPath = "~/.ssh/id_ed25519"
            }
        },
        IsDryRun = dryRun
    };

    [Fact]
    public async Task ExecuteAsync_DryRun_DoesNotConnect()
    {
        var ssh = new Mock<ISshService>();
        ssh.Setup(s => s.ExecuteAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 0, Stdout = "root" });

        var sut = new ConnectAndVerifyPhase(ssh.Object, NullLogger<ConnectAndVerifyPhase>.Instance);
        var context = BuildContext(dryRun: true);

        await sut.ExecuteAsync(context);

        ssh.Verify(s => s.ConnectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_DryRun_PopulatesServerInfo()
    {
        var ssh = new Mock<ISshService>();

        var sut = new ConnectAndVerifyPhase(ssh.Object, NullLogger<ConnectAndVerifyPhase>.Instance);
        var context = BuildContext(dryRun: true);

        await sut.ExecuteAsync(context);

        // In dry-run mode, SSH is never called; ServerInfo is populated with dry-run placeholder strings.
        context.ServerInfo.Should().NotBeNull();
        context.ServerInfo!.ConnectedUser.Should().NotBeEmpty();
        context.ServerInfo.Hostname.Should().NotBeEmpty();
        ssh.Verify(s => s.ExecuteAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_AddedToCompletedPhases()
    {
        var ssh = new Mock<ISshService>();
        ssh.Setup(s => s.ExecuteAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 0, Stdout = "root" });

        var sut = new ConnectAndVerifyPhase(ssh.Object, NullLogger<ConnectAndVerifyPhase>.Instance);
        var context = BuildContext(dryRun: true);

        await sut.ExecuteAsync(context);

        context.CompletedPhases.Should().Contain("connect-and-verify");
    }

    [Fact]
    public void Properties_HaveCorrectValues()
    {
        var ssh = new Mock<ISshService>();
        var sut = new ConnectAndVerifyPhase(ssh.Object, NullLogger<ConnectAndVerifyPhase>.Instance);

        sut.Name.Should().Be("connect-and-verify");
        sut.Order.Should().Be(10);
        sut.IsRequired.Should().BeTrue();
    }
}
