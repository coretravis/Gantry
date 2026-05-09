namespace Gantry.Core.Models;

public class ServerInfo
{
    public string Hostname { get; set; } = string.Empty;
    public string OsName { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public long TotalMemoryMb { get; set; }
    public long AvailableMemoryMb { get; set; }
    public long TotalDiskGb { get; set; }
    public long AvailableDiskGb { get; set; }
    public string ConnectedUser { get; set; } = string.Empty;
    public bool HasRootAccess { get; set; }
    public bool HasPasswordlessSudo { get; set; }
}
