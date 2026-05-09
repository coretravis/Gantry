namespace Gantry.Core.Models;

public class PluginConfig
{
    private readonly Dictionary<string, string> _values;
    private readonly bool _configured;

    private PluginConfig(Dictionary<string, string>? values, bool configured)
    {
        _values = values != null
            ? new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _configured = configured;
    }

    public static PluginConfig Empty => new(null, configured: false);

    public static PluginConfig From(Dictionary<string, string> values) =>
        new(values, configured: true);

    public bool IsEnabled =>
        _configured &&
        (!_values.TryGetValue("enabled", out var v) || v.Equals("true", StringComparison.OrdinalIgnoreCase));

    public string? Get(string key) =>
        _values.TryGetValue(key, out var v) ? v : null;

    public string GetOrDefault(string key, string defaultValue) =>
        _values.TryGetValue(key, out var v) ? v : defaultValue;
}
