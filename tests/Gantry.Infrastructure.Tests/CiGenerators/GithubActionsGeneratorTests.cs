using FluentAssertions;
using Gantry.Core.Models;
using Gantry.Infrastructure.CiGenerators;
using Gantry.Infrastructure.Templates;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gantry.Infrastructure.Tests.CiGenerators;

public class GithubActionsGeneratorTests
{
    private readonly GithubActionsGenerator _sut;

    public GithubActionsGeneratorTests()
    {
        var engine = new TemplateEngine();
        _sut = new GithubActionsGenerator(engine, NullLogger<GithubActionsGenerator>.Instance);
    }

    private static DeployConfig BuildConfig() => new()
    {
        App = new AppConfig { Name = "my-app", ProjectPath = "src/MyApp/MyApp.csproj", Port = 5000, HealthCheckPath = "/health" },
        Server = new ServerConfig { Host = "192.168.1.1" },
        Runtime = new RuntimeConfig { Version = "8.0" },
        Domain = new DomainConfig { Name = "myapp.com" },
        Ci = new CiConfig { Branch = "main", RunTests = true, WorkflowPath = ".github/workflows/deploy.yml" }
    };

    [Fact]
    public void GenerateWorkflow_ContainsAppName()
    {
        var config = BuildConfig();
        var workflow = _sut.GenerateWorkflow(config);
        workflow.Should().Contain("my-app");
    }

    [Fact]
    public void GenerateWorkflow_ContainsBranchTrigger()
    {
        var config = BuildConfig();
        var workflow = _sut.GenerateWorkflow(config);
        workflow.Should().Contain("main");
    }

    [Fact]
    public void GenerateWorkflow_ContainsDotNetVersion()
    {
        var config = BuildConfig();
        var workflow = _sut.GenerateWorkflow(config);
        workflow.Should().Contain("8.0");
    }

    [Fact]
    public void GenerateWorkflow_ContainsProjectPath()
    {
        var config = BuildConfig();
        var workflow = _sut.GenerateWorkflow(config);
        workflow.Should().Contain("src/MyApp/MyApp.csproj");
    }

    [Fact]
    public void GenerateWorkflow_WithDomain_UsesHttpsHealthCheckUrl()
    {
        var config = BuildConfig();
        var workflow = _sut.GenerateWorkflow(config);
        workflow.Should().Contain("https://myapp.com/health");
    }

    [Fact]
    public void GenerateWorkflow_WithoutDomain_UsesIpHealthCheckUrl()
    {
        var config = BuildConfig();
        config.Domain.Name = string.Empty;
        var workflow = _sut.GenerateWorkflow(config);
        workflow.Should().Contain("http://192.168.1.1:5000");
    }

    [Fact]
    public void GenerateWorkflow_WithRunTests_ContainsTestStep()
    {
        var config = BuildConfig();
        config.Ci.RunTests = true;
        var workflow = _sut.GenerateWorkflow(config);
        workflow.Should().Contain("dotnet test");
    }

    [Fact]
    public void GenerateWorkflow_WithoutRunTests_DoesNotContainTestStep()
    {
        var config = BuildConfig();
        config.Ci.RunTests = false;
        var workflow = _sut.GenerateWorkflow(config);
        workflow.Should().NotContain("dotnet test");
    }

    [Fact]
    public void GenerateWorkflow_ContainsSecretReferences()
    {
        var config = BuildConfig();
        var workflow = _sut.GenerateWorkflow(config);
        workflow.Should().Contain("secrets.DO_HOST");
        workflow.Should().Contain("secrets.DO_USER");
        workflow.Should().Contain("secrets.DO_SSH_KEY");
    }

    [Fact]
    public void GetWorkflowPath_ReturnsConfiguredPath()
    {
        var config = BuildConfig();
        _sut.GetWorkflowPath(config).Should().Be(".github/workflows/deploy.yml");
    }

    [Fact]
    public void GeneratedWorkflow_UsesReleasesDirectory_NotAppDirectory()
    {
        var config = BuildConfig();
        var workflow = _sut.GenerateWorkflow(config);
        workflow.Should().Contain("/var/www/my-app/releases/");
    }

    [Fact]
    public void GeneratedWorkflow_ContainsSymlinkActivationStep()
    {
        var config = BuildConfig();
        var workflow = _sut.GenerateWorkflow(config);
        workflow.Should().Contain("ln -sfn");
    }

    [Fact]
    public void GeneratedWorkflow_PrunesReleasesUsingConfiguredCount()
    {
        var config = BuildConfig();
        config.App.ReleasesToKeep = 5;
        var workflow = _sut.GenerateWorkflow(config);
        workflow.Should().Contain("tail -n +$((5 + 1))");
    }

    [Fact]
    public void GeneratedWorkflow_DoesNotReferenceDeprecatedAppPath()
    {
        var config = BuildConfig();
        var workflow = _sut.GenerateWorkflow(config);
        workflow.Should().NotContain("/var/www/my-app/app");
    }
}
