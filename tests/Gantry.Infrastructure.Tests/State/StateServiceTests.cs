using FluentAssertions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Gantry.Infrastructure.State;
using Moq;

namespace Gantry.Infrastructure.Tests.State;

public class StateServiceTests
{
    private readonly Mock<ISshService> _ssh = new();
    private readonly StateService _sut = new();

    private const string AppName = "test-app";
    private const string StatePath = "/var/www/test-app/gantry.json";

    [Fact]
    public async Task Read_WhenFileAbsent_ReturnsDefaultState()
    {
        _ssh.Setup(s => s.FileExistsAsync(StatePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _sut.ReadAsync(_ssh.Object, AppName);

        result.Should().NotBeNull();
        result.GantryVersion.Should().Be("1.0.0");
        result.CurrentRelease.Should().BeEmpty();
        result.Plugins.Should().BeEmpty();

        _ssh.Verify(s => s.DownloadStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Read_WithValidJson_DeserialisesCorrently()
    {
        const string json = """
            {
              "gantry_version": "1.0.0",
              "current_release": "abc1234-20250510-143022",
              "plugins": {
                "postgres": {
                  "installed": true,
                  "version": "16",
                  "installed_at": "2025-05-10T14:30:22+00:00"
                }
              }
            }
            """;

        _ssh.Setup(s => s.FileExistsAsync(StatePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _ssh.Setup(s => s.DownloadStringAsync(StatePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var result = await _sut.ReadAsync(_ssh.Object, AppName);

        result.GantryVersion.Should().Be("1.0.0");
        result.CurrentRelease.Should().Be("abc1234-20250510-143022");
        result.Plugins.Should().ContainKey("postgres");
        result.Plugins["postgres"].Installed.Should().BeTrue();
        result.Plugins["postgres"].Version.Should().Be("16");
    }

    [Fact]
    public async Task Write_SerializesAndUploadsJson()
    {
        var state = new GantryState
        {
            GantryVersion = "1.0.0",
            CurrentRelease = "abc1234-20250510-143022",
            Plugins = new Dictionary<string, InstalledPlugin>
            {
                ["postgres"] = new() { Installed = true, Version = "16" }
            }
        };

        string? uploaded = null;
        _ssh.Setup(s => s.UploadStringAsync(It.IsAny<string>(), StatePath, It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((content, _, _) => uploaded = content)
            .Returns(Task.CompletedTask);

        await _sut.WriteAsync(_ssh.Object, AppName, state);

        _ssh.Verify(s => s.UploadStringAsync(It.IsAny<string>(), StatePath, It.IsAny<CancellationToken>()), Times.Once);
        uploaded.Should().Contain("current_release");
        uploaded.Should().Contain("abc1234-20250510-143022");
        uploaded.Should().Contain("gantry_version");
    }

    [Fact]
    public async Task RoundTrip_WriteAndRead_ReturnsIdenticalState()
    {
        var original = new GantryState
        {
            GantryVersion = "1.0.0",
            CurrentRelease = "abc1234-20250510-143022",
            Plugins = new Dictionary<string, InstalledPlugin>
            {
                ["postgres"] = new()
                {
                    Installed = true,
                    Version = "16",
                    InstalledAt = new DateTimeOffset(2025, 5, 10, 14, 30, 22, TimeSpan.Zero)
                }
            }
        };

        string? stored = null;
        _ssh.Setup(s => s.UploadStringAsync(It.IsAny<string>(), StatePath, It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((content, _, _) => stored = content)
            .Returns(Task.CompletedTask);
        _ssh.Setup(s => s.FileExistsAsync(StatePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _ssh.Setup(s => s.DownloadStringAsync(StatePath, It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(stored!));

        await _sut.WriteAsync(_ssh.Object, AppName, original);
        var result = await _sut.ReadAsync(_ssh.Object, AppName);

        result.GantryVersion.Should().Be(original.GantryVersion);
        result.CurrentRelease.Should().Be(original.CurrentRelease);
        result.Plugins.Should().ContainKey("postgres");
        result.Plugins["postgres"].Installed.Should().Be(original.Plugins["postgres"].Installed);
        result.Plugins["postgres"].Version.Should().Be(original.Plugins["postgres"].Version);
        result.Plugins["postgres"].InstalledAt.Should().Be(original.Plugins["postgres"].InstalledAt);
    }
}
