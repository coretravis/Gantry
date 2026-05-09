namespace Gantry.Core.Models;

public class ProvisioningContext
{
    public DeployConfig Config { get; set; } = new();
    public bool IsDryRun { get; set; }
    public IProgress<PhaseProgress>? Progress { get; set; }
    public CancellationToken CancellationToken { get; set; }
    public ServerInfo? ServerInfo { get; set; }
    public List<string> CompletedPhases { get; } = [];
    public List<(string Phase, string Reason)> FailedOptionalPhases { get; } = [];
    public string? GeneratedDeployKeyPublic { get; set; }
    public string? GeneratedWorkflowPath { get; set; }
    public int CurrentPhaseNumber { get; set; }
    public int TotalPhases { get; set; }
}
