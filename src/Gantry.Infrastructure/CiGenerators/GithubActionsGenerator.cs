using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gantry.Infrastructure.CiGenerators;

public class GithubActionsGenerator : ICiGenerator
{
    // Pin third-party actions to specific versions. Update here when upgrading.
    private const string ScpAction = "appleboy/scp-action@v0.1.7";
    private const string SshAction = "appleboy/ssh-action@v1.0.3";

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

        var runTestsStep = config.Ci.RunTests
            ? "- name: Test\n        run: dotnet test --no-build --configuration Release"
            : string.Empty;

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
            ["run_tests_step"] = runTestsStep,
            ["scp_action"] = ScpAction,
            ["ssh_action"] = SshAction
        };

        var workflow = _templates.Render("github-actions.yml", tokens);
        _logger.LogDebug("Generated GitHub Actions workflow for {AppName}", config.App.Name);
        return workflow;
    }

    public string GetWorkflowPath(DeployConfig config) => config.Ci.WorkflowPath;
}
