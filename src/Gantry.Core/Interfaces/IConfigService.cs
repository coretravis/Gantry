using Gantry.Core.Models;

namespace Gantry.Core.Interfaces;

/// <summary>Reads and writes .deploy.yml configuration files.</summary>
public interface IConfigService
{
    DeployConfig Load(string path);
    void Save(DeployConfig config, string path);
    bool Exists(string path);
    DeployConfig CreateDefault();
}
