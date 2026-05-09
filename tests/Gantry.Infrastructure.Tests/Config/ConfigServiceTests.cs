using FluentAssertions;
using Gantry.Core.Models;
using Gantry.Infrastructure.Config;

namespace Gantry.Infrastructure.Tests.Config;

public class ConfigServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"gantry-test-{Guid.NewGuid():N}");
    private readonly ConfigService _sut = new();

    public ConfigServiceTests() => Directory.CreateDirectory(_tempDir);

    [Fact]
    public void Save_ThenLoad_RoundTripsCorrectly()
    {
        var path = Path.Combine(_tempDir, ".deploy.yml");
        var config = new DeployConfig
        {
            Server = new ServerConfig { Host = "192.168.1.1", DeployUser = "deployer" },
            App = new AppConfig { Name = "my-app", Port = 8080 },
            Runtime = new RuntimeConfig { Ecosystem = "dotnet", Version = "8.0" },
            Domain = new DomainConfig { Name = "example.com", Ssl = true, SslEmail = "test@example.com" },
            Ci = new CiConfig { Branch = "main", Platform = "github_actions" }
        };

        _sut.Save(config, path);
        var loaded = _sut.Load(path);

        loaded.Server.Host.Should().Be("192.168.1.1");
        loaded.Server.DeployUser.Should().Be("deployer");
        loaded.App.Name.Should().Be("my-app");
        loaded.App.Port.Should().Be(8080);
        loaded.Runtime.Version.Should().Be("8.0");
        loaded.Domain.Name.Should().Be("example.com");
        loaded.Domain.Ssl.Should().BeTrue();
        loaded.Ci.Branch.Should().Be("main");
    }

    [Fact]
    public void Load_FileNotFound_ThrowsFileNotFoundException()
    {
        var act = () => _sut.Load(Path.Combine(_tempDir, "nonexistent.yml"));
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Exists_WhenFilePresent_ReturnsTrue()
    {
        var path = Path.Combine(_tempDir, ".deploy.yml");
        _sut.Save(new DeployConfig(), path);
        _sut.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void Exists_WhenFileMissing_ReturnsFalse()
    {
        _sut.Exists(Path.Combine(_tempDir, "missing.yml")).Should().BeFalse();
    }

    [Fact]
    public void Save_WritesHeader_ContainsGantryComment()
    {
        var path = Path.Combine(_tempDir, ".deploy.yml");
        _sut.Save(new DeployConfig(), path);
        var content = File.ReadAllText(path);
        content.Should().Contain("Gantry deployment configuration");
    }

    [Fact]
    public void Save_EnvironmentVars_RoundTripsCorrectly()
    {
        var path = Path.Combine(_tempDir, ".deploy.yml");
        var config = new DeployConfig();
        config.Environment["CUSTOM_KEY"] = "custom_value";

        _sut.Save(config, path);
        var loaded = _sut.Load(path);

        loaded.Environment.Should().ContainKey("CUSTOM_KEY");
        loaded.Environment["CUSTOM_KEY"].Should().Be("custom_value");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);
}
