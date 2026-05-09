namespace Gantry.Core.Models;

public class PhaseProgress
{
    public string PhaseName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public PhaseStatus Status { get; init; }
    public int PhaseNumber { get; init; }
    public int TotalPhases { get; init; }
}

public enum PhaseStatus
{
    Starting,
    Running,
    Completed,
    Failed,
    Skipped,
    RollingBack,
    Warning
}
