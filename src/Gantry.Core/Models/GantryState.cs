namespace Gantry.Core.Models;

public class GantryState
{
    public string GantryVersion { get; set; } = string.Empty;
    public string CurrentRelease { get; set; } = string.Empty;
    public Dictionary<string, InstalledPlugin> Plugins { get; set; } = new();
}

public class InstalledPlugin
{
    public bool Installed { get; set; }
    public string Version { get; set; } = string.Empty;
    public DateTimeOffset InstalledAt { get; set; }
}
