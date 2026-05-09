using FluentAssertions;
using Gantry.Core.Models;
using Gantry.Core.Validation;

namespace Gantry.Core.Tests.Validation;

public class DeployConfigValidatorTests
{
    private readonly DeployConfigValidator _sut = new();

    private static DeployConfig ValidConfig() => new()
    {
        Server = new() { Host = "192.168.1.1", SshKeyPath = "~/.ssh/id_ed25519" },
        App = new() { Name = "my-app", Port = 5000 },
        Runtime = new() { Ecosystem = "dotnet", Version = "8.0" },
        Domain = new() { Name = "example.com", Ssl = true, SslEmail = "admin@example.com" },
        Ci = new() { Platform = "github_actions", Branch = "main" }
    };

    [Fact]
    public void IsValid_WithValidConfig_ReturnsTrue()
    {
        _sut.IsValid(ValidConfig()).Should().BeTrue();
    }

    [Fact]
    public void Validate_MissingHost_ReturnsError()
    {
        var config = ValidConfig();
        config.Server.Host = string.Empty;
        _sut.Validate(config).Should().Contain(e => e.Contains("server.host"));
    }

    [Fact]
    public void Validate_MissingAppName_ReturnsError()
    {
        var config = ValidConfig();
        config.App.Name = string.Empty;
        _sut.Validate(config).Should().Contain(e => e.Contains("app.name"));
    }

    [Fact]
    public void Validate_AppNameWithUppercase_ReturnsError()
    {
        var config = ValidConfig();
        config.App.Name = "MyApp";
        _sut.Validate(config).Should().Contain(e => e.Contains("app.name"));
    }

    [Fact]
    public void Validate_AppNameTooLong_ReturnsError()
    {
        var config = ValidConfig();
        config.App.Name = new string('a', 51);
        _sut.Validate(config).Should().Contain(e => e.Contains("app.name"));
    }

    [Theory]
    [InlineData(1023)]
    [InlineData(65536)]
    [InlineData(0)]
    public void Validate_InvalidPort_ReturnsError(int port)
    {
        var config = ValidConfig();
        config.App.Port = port;
        _sut.Validate(config).Should().Contain(e => e.Contains("app.port"));
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(5000)]
    [InlineData(65535)]
    public void Validate_ValidPort_NoPortError(int port)
    {
        var config = ValidConfig();
        config.App.Port = port;
        _sut.Validate(config).Should().NotContain(e => e.Contains("app.port"));
    }

    [Fact]
    public void Validate_SslEnabledWithoutEmail_ReturnsError()
    {
        var config = ValidConfig();
        config.Domain.Ssl = true;
        config.Domain.SslEmail = string.Empty;
        _sut.Validate(config).Should().Contain(e => e.Contains("ssl_email"));
    }

    [Fact]
    public void Validate_InvalidEmailFormat_ReturnsError()
    {
        var config = ValidConfig();
        config.Domain.SslEmail = "not-an-email";
        _sut.Validate(config).Should().Contain(e => e.Contains("ssl_email"));
    }

    [Fact]
    public void Validate_SslDisabledMissingEmail_NoEmailError()
    {
        var config = ValidConfig();
        config.Domain.Ssl = false;
        config.Domain.SslEmail = string.Empty;
        _sut.Validate(config).Should().NotContain(e => e.Contains("ssl_email"));
    }

    [Fact]
    public void Validate_MultipleErrors_ReturnsAllErrors()
    {
        var config = new DeployConfig();
        var errors = _sut.Validate(config);
        errors.Count.Should().BeGreaterThan(1);
    }
}
