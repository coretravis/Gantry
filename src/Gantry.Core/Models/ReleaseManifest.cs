namespace Gantry.Core.Models;

public class ReleaseManifest
{
    public string Current { get; set; } = string.Empty;
    public List<Release> Releases { get; set; } = [];
}

public class Release
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset DeployedAt { get; set; }
    public string DeployedBy { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string HealthCheck { get; set; } = "unknown";
}
