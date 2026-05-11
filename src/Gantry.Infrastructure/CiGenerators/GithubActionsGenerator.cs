using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gantry.Infrastructure.CiGenerators;

public class GithubActionsGenerator : ICiGenerator
{
    private readonly ITemplateEngine _templates;
    private readonly ILogger<GithubActionsGenerator> _logger;

    public GithubActionsGenerator(ITemplateEngine templates, ILogger<GithubActionsGenerator> logger)
    {
        _templates = templates;
        _logger = logger;
    }

    public string Platform => "github_actions";

    public string GenerateWorkflow(DeployConfig config)
    {
        var deployPath = string.IsNullOrWhiteSpace(config.App.DeployPath)
            ? $"/var/www/{config.App.Name}/app"
            : config.App.DeployPath;

        var healthCheckUrl = config.Domain.HasDomain
            ? $"https://{config.Domain.Name}{config.App.HealthCheckPath}"
            : $"http://{config.Server.Host}:{config.App.Port}{config.App.HealthCheckPath}";

        var (testStepName, testStepRun) = config.Ci.RunTests
            ? ("Test", "dotnet test --no-build --configuration Release")
            : ("Skip tests", "echo 'Tests disabled'");

        var tokens = new Dictionary<string, string>
        {
            ["app_name"] = config.App.Name,
            ["server_host"] = config.Server.Host,
            ["branch"] = config.Ci.Branch,
            ["runtime_version"] = config.Runtime.Version,
            ["project_path"] = config.App.ProjectPath,
            ["deploy_path"] = deployPath,
            ["service_name"] = config.App.Name,
            ["health_check_url"] = healthCheckUrl,
            ["health_check_retries"] = "6",
            ["releases_to_keep"] = config.App.ReleasesToKeep.ToString(),
            ["test_step_name"] = testStepName,
            ["test_step_run"] = testStepRun
        };

        var workflow = _templates.Render("github-actions.yml", tokens);
        _logger.LogDebug("Generated GitHub Actions workflow for {AppName}", config.App.Name);
        return workflow;
    }

    public string GetWorkflowPath(DeployConfig config) => config.Ci.WorkflowPath;
}
