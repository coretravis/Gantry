using FluentAssertions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Gantry.Core.Phases;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Gantry.Core.Tests.Phases;

public class ProcessManagerSetupPhaseTests
{
    private readonly Mock<ISshService> _ssh = new();
    private readonly Mock<IProcessManager> _processManager = new();
    private readonly ProcessManagerSetupPhase _sut;

    public ProcessManagerSetupPhaseTests()
    {
        _sut = new ProcessManagerSetupPhase(_ssh.Object, _processManager.Object,
            NullLogger<ProcessManagerSetupPhase>.Instance);

        _ssh.Setup(s => s.ExecuteAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 0, Stdout = string.Empty });

        _ssh.Setup(s => s.ExecuteAsync(
                It.Is<string>(c => c.Contains("test -L")),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 0, Stdout = "no" });

        _ssh.Setup(s => s.DirectoryExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    private static ProvisioningContext BuildContext(bool dryRun = false) => new()
    {
        Config = new DeployConfig
        {
            App = new AppConfig { Name = "test-app" },
            Server = new ServerConfig { DeployUser = "deployer" }
        },
        IsDryRun = dryRun
    };

    [Fact]
    public async Task Setup_WhenOldLayoutDetected_PerformsMigration()
    {
        _ssh.Setup(s => s.DirectoryExistsAsync(
                It.Is<string>(p => p.Contains("/app")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _ssh.Setup(s => s.ExecuteAsync(
                It.Is<string>(c => c.Contains("test -L")),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 0, Stdout = "no" });

        await _sut.ExecuteAsync(BuildContext());

        _ssh.Verify(
            s => s.ExecuteAsync(
                It.Is<string>(cmd => cmd.Contains("mv") && cmd.Contains("/app") && cmd.Contains("pre-symlink-")),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Setup_WhenCurrentSymlinkExists_SkipsMigration()
    {
        _ssh.Setup(s => s.DirectoryExistsAsync(
                It.Is<string>(p => p.Contains("/app")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _ssh.Setup(s => s.ExecuteAsync(
                It.Is<string>(c => c.Contains("test -L")),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 0, Stdout = "yes" });

        await _sut.ExecuteAsync(BuildContext());

        _ssh.Verify(
            s => s.ExecuteAsync(
                It.Is<string>(cmd => cmd.Contains("mv") && cmd.Contains("/app") && cmd.Contains("pre-symlink-")),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
