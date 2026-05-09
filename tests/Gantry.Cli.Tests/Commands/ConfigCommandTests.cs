using FluentAssertions;
using Gantry.Cli.Commands;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Gantry.Core.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Gantry.Cli.Tests.Commands;

public class ConfigCommandTests
{
    private readonly Mock<IConfigService> _configService = new();
    private readonly DeployConfigValidator _validator = new();
    private readonly ConfigCommandHandler _sut;

    public ConfigCommandTests()
    {
        _sut = new ConfigCommandHandler(_configService.Object, _validator, NullLogger<ConfigCommandHandler>.Instance);
    }

    [Fact]
    public void Show_WhenConfigExists_ReturnsZero()
    {
        _configService.Setup(c => c.Exists(It.IsAny<string>())).Returns(true);
        _configService.Setup(c => c.Load(It.IsAny<string>())).Returns(new DeployConfig
        {
            Server = new ServerConfig { Host = "1.2.3.4" },
            App = new AppConfig { Name = "test-app" }
        });

        var result = _sut.Show(".deploy.yml");
        result.Should().Be(0);
    }

    [Fact]
    public void Show_WhenConfigMissing_ReturnsOne()
    {
        _configService.Setup(c => c.Exists(It.IsAny<string>())).Returns(false);
        var result = _sut.Show(".deploy.yml");
        result.Should().Be(1);
    }

    [Fact]
    public void Validate_WithValidConfig_ReturnsZero()
    {
        _configService.Setup(c => c.Exists(It.IsAny<string>())).Returns(true);
        _configService.Setup(c => c.Load(It.IsAny<string>())).Returns(new DeployConfig
        {
            Server = new ServerConfig { Host = "1.2.3.4", SshKeyPath = "~/.ssh/id" },
            App = new AppConfig { Name = "my-app", Port = 5000 },
            Runtime = new RuntimeConfig { Ecosystem = "dotnet", Version = "8.0" },
            Domain = new DomainConfig { Ssl = false },
            Ci = new CiConfig { Platform = "github_actions", Branch = "main" }
        });

        var result = _sut.Validate(".deploy.yml");
        result.Should().Be(0);
    }

    [Fact]
    public void Validate_WithInvalidConfig_ReturnsOne()
    {
        _configService.Setup(c => c.Exists(It.IsAny<string>())).Returns(true);
        _configService.Setup(c => c.Load(It.IsAny<string>())).Returns(new DeployConfig());

        var result = _sut.Validate(".deploy.yml");
        result.Should().Be(1);
    }

    [Fact]
    public void Validate_WhenConfigMissing_ReturnsOne()
    {
        _configService.Setup(c => c.Exists(It.IsAny<string>())).Returns(false);
        var result = _sut.Validate(".deploy.yml");
        result.Should().Be(1);
    }
}
