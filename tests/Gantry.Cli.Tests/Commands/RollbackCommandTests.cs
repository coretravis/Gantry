using FluentAssertions;
using Gantry.Cli.Commands;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Gantry.Cli.Tests.Commands;

public class RollbackCommandTests
{
    private readonly Mock<ISshService> _ssh = new();
    private readonly Mock<IProcessManager> _processManager = new();
    private readonly Mock<IConfigService> _configService = new();
    private readonly Mock<IStateService> _stateService = new();

    public RollbackCommandTests()
    {
        _configService.Setup(c => c.Load(It.IsAny<string>())).Returns(new DeployConfig
        {
            Server = new ServerConfig { Host = "1.2.3.4" },
            App = new AppConfig { Name = "test-app" }
        });

        _ssh.Setup(s => s.ExecuteAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 0 });
    }

    private RollbackCommandHandler CreateSut() =>
        new(_ssh.Object, _processManager.Object, _configService.Object,
            _stateService.Object, NullLogger<RollbackCommandHandler>.Instance);

    [Fact]
    public async Task Rollback_WhenSelectingCurrentRelease_DoesNotRestart()
    {
        const string release = "abc1234-20250510-143022";

        _stateService
            .Setup(s => s.ReadAsync(_ssh.Object, "test-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GantryState { CurrentRelease = release });

        _ssh.Setup(s => s.ExecuteAsync(
                It.Is<string>(cmd => cmd.StartsWith("ls")),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult
            {
                ExitCode = 0,
                Stdout = $"/var/www/test-app/releases/{release}/\n"
            });

        var result = await CreateSut().ExecuteAsync(".deploy.yml", release);

        result.Should().Be(0);
        _processManager.Verify(
            p => p.StopAsync(It.IsAny<ISshService>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Rollback_WhenStateFileAbsent_ProceedsNormally()
    {
        const string release = "abc1234-20250510-143022";

        _stateService
            .Setup(s => s.ReadAsync(_ssh.Object, "test-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GantryState()); // empty — no current release recorded

        _ssh.Setup(s => s.ExecuteAsync(
                It.Is<string>(cmd => cmd.StartsWith("ls")),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult
            {
                ExitCode = 0,
                Stdout = $"/var/www/test-app/releases/{release}/\n"
            });

        _processManager
            .Setup(p => p.IsActiveAsync(_ssh.Object, "test-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateSut().ExecuteAsync(".deploy.yml", release);

        result.Should().Be(0);
        _processManager.Verify(
            p => p.StopAsync(It.IsAny<ISshService>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Rollback_UsesSymlinkSwap_NotFileCopy()
    {
        const string prevRelease = "abc1234-20250510-130000";
        const string currRelease = "def5678-20250510-143022";

        _stateService
            .Setup(s => s.ReadAsync(_ssh.Object, "test-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GantryState { CurrentRelease = currRelease });

        _ssh.Setup(s => s.ExecuteAsync(
                It.Is<string>(cmd => cmd.StartsWith("ls")),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult
            {
                ExitCode = 0,
                Stdout = $"/var/www/test-app/releases/{currRelease}/\n/var/www/test-app/releases/{prevRelease}/\n"
            });

        _processManager
            .Setup(p => p.IsActiveAsync(_ssh.Object, "test-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateSut().ExecuteAsync(".deploy.yml", prevRelease);

        result.Should().Be(0);
        _ssh.Verify(
            s => s.ExecuteAsync(
                It.Is<string>(cmd => cmd.Contains("ln -sfn") && cmd.Contains(prevRelease)),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _ssh.Verify(
            s => s.ExecuteAsync(
                It.Is<string>(cmd => cmd.Contains("cp -r")),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Rollback_UpdatesStateFileCurrentRelease()
    {
        const string prevRelease = "abc1234-20250510-130000";
        const string currRelease = "def5678-20250510-143022";

        _stateService
            .Setup(s => s.ReadAsync(_ssh.Object, "test-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GantryState { CurrentRelease = currRelease });

        _ssh.Setup(s => s.ExecuteAsync(
                It.Is<string>(cmd => cmd.StartsWith("ls")),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult
            {
                ExitCode = 0,
                Stdout = $"/var/www/test-app/releases/{currRelease}/\n/var/www/test-app/releases/{prevRelease}/\n"
            });

        _processManager
            .Setup(p => p.IsActiveAsync(_ssh.Object, "test-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await CreateSut().ExecuteAsync(".deploy.yml", prevRelease);

        _stateService.Verify(
            s => s.WriteAsync(
                _ssh.Object,
                "test-app",
                It.Is<GantryState>(state => state.CurrentRelease == prevRelease),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
