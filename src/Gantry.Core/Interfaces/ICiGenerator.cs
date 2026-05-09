using Gantry.Core.Models;

namespace Gantry.Core.Interfaces;

/// <summary>Generates a CI/CD pipeline workflow file from deployment configuration.</summary>
public interface ICiGenerator
{
    string Platform { get; }
    string GenerateWorkflow(DeployConfig config);
    string GetWorkflowPath(DeployConfig config);
}
