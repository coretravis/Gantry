using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Gantry.Infrastructure.Config;

public class ConfigService : IConfigService
{
    private readonly IDeserializer _deserializer;
    private readonly ISerializer _serializer;

    public ConfigService()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        _serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();
    }

    public DeployConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Configuration file not found at '{path}'.");

        var yaml = File.ReadAllText(path);
        return _deserializer.Deserialize<DeployConfig>(yaml);
    }

    public void Save(DeployConfig config, string path)
    {
        var yaml = BuildFileHeader() + _serializer.Serialize(config);
        File.WriteAllText(path, yaml);
    }

    public bool Exists(string path) => File.Exists(path);

    public DeployConfig CreateDefault() => new();

    private static string BuildFileHeader() =>
        "# Gantry deployment configuration\n" +
        $"# Generated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC\n" +
        "# WARNING: Do not commit this file if it contains sensitive key paths.\n\n";
}
