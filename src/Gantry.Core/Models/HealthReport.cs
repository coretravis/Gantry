namespace Gantry.Core.Models;

public enum HealthStatus { Healthy, Warning, Critical, NotApplicable }

public record HealthCheck(
    string Component,
    HealthStatus Status,
    string Detail,
    string? Remediation = null);

public class HealthReport
{
    public IReadOnlyList<HealthCheck> Checks { get; }
    public HealthStatus Overall { get; }
    public string AppName { get; }
    public string ServerHost { get; }

    public HealthReport(string appName, string serverHost, IReadOnlyList<HealthCheck> checks)
    {
        AppName = appName;
        ServerHost = serverHost;
        Checks = checks;
        Overall = checks
            .Where(c => c.Status != HealthStatus.NotApplicable)
            .Select(c => c.Status)
            .DefaultIfEmpty(HealthStatus.Healthy)
            .Max();
    }

    /// <summary>Maps Overall to a gantry status exit code: 0=healthy, 1=critical, 2=degraded.</summary>
    public int ExitCode => Overall switch
    {
        HealthStatus.Critical => 1,
        HealthStatus.Warning => 2,
        _ => 0
    };
}
