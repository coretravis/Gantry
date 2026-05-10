using FluentAssertions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Gantry.Core.Phases;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Gantry.Core.Tests.Phases;

public class OsHardeningPhaseTests
{
    private readonly Mock<ISshService> _ssh = new();
    private readonly OsHardeningPhase _sut;

    private const string SshdConfigPath = "/etc/ssh/sshd_config";

    private const string UnhardenedConfig =
        "Port 22\n" +
        "LoginGraceTime 2m\n" +
        "#PermitRootLogin yes\n" +
        "#PubkeyAuthentication yes\n" +
        "#PasswordAuthentication yes\n";

    private const string HardenedConfig =
        "Port 22\n" +
        "LoginGraceTime 2m\n" +
        "PermitRootLogin prohibit-password\n" +
        "PubkeyAuthentication yes\n" +
        "PasswordAuthentication no\n";

    public OsHardeningPhaseTests()
    {
        _sut = new OsHardeningPhase(_ssh.Object, NullLogger<OsHardeningPhase>.Instance);

        _ssh.Setup(s => s.ExecuteAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 0, Stdout = string.Empty });

        // Timezone check: return UTC to match config so the set-timezone command is skipped
        _ssh.Setup(s => s.ExecuteAsync(
                It.Is<string>(c => c.Contains("timedatectl show")),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult { ExitCode = 0, Stdout = "UTC" });

        _ssh.Setup(s => s.FileExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _ssh.Setup(s => s.UploadStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static ProvisioningContext BuildContext(bool dryRun = false) => new()
    {
        Config = new DeployConfig
        {
            Server = new ServerConfig { DeployUser = "deployer", Timezone = "UTC", Port = 22 },
            App = new AppConfig { Name = "test-app" }
        },
        IsDryRun = dryRun,
        GeneratedDeployKeyPublic = null
    };

    [Fact]
    public async Task HardenSsh_WhenAlreadyHardened_DoesNotUpload()
    {
        _ssh.Setup(s => s.FileExistsAsync(SshdConfigPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _ssh.Setup(s => s.DownloadStringAsync(SshdConfigPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(HardenedConfig);

        await _sut.ExecuteAsync(BuildContext());

        _ssh.Verify(
            s => s.UploadStringAsync(It.IsAny<string>(), SshdConfigPath, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HardenSsh_WithCommentedDefaults_ProducesHardenedContent()
    {
        _ssh.Setup(s => s.FileExistsAsync(SshdConfigPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _ssh.Setup(s => s.DownloadStringAsync(SshdConfigPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UnhardenedConfig);

        await _sut.ExecuteAsync(BuildContext());

        _ssh.Verify(
            s => s.UploadStringAsync(
                It.Is<string>(c =>
                    c.Contains("PasswordAuthentication no") &&
                    c.Contains("PermitRootLogin prohibit-password") &&
                    c.Contains("PubkeyAuthentication yes") &&
                    !c.Contains("#PasswordAuthentication") &&
                    !c.Contains("#PermitRootLogin") &&
                    !c.Contains("#PubkeyAuthentication")),
                SshdConfigPath,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HardenSsh_WhenContentChanges_ReloadsSshd()
    {
        _ssh.Setup(s => s.FileExistsAsync(SshdConfigPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _ssh.Setup(s => s.DownloadStringAsync(SshdConfigPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UnhardenedConfig);

        await _sut.ExecuteAsync(BuildContext());

        _ssh.Verify(
            s => s.ExecuteAsync(
                It.Is<string>(c => c.Contains("systemctl reload ssh")),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HardenSsh_WhenContentUnchanged_SkipsReload()
    {
        _ssh.Setup(s => s.FileExistsAsync(SshdConfigPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _ssh.Setup(s => s.DownloadStringAsync(SshdConfigPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(HardenedConfig);

        await _sut.ExecuteAsync(BuildContext());

        _ssh.Verify(
            s => s.ExecuteAsync(
                It.Is<string>(c => c.Contains("systemctl reload ssh")),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HardenSsh_DryRun_DoesNotTouchServer()
    {
        await _sut.ExecuteAsync(BuildContext(dryRun: true));

        _ssh.Verify(s => s.DownloadStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _ssh.Verify(s => s.UploadStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
