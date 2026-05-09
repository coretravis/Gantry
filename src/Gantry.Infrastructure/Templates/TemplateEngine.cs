using System.Reflection;
using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;

namespace Gantry.Infrastructure.Templates;

public class TemplateEngine : ITemplateEngine
{
    private static readonly Assembly Assembly = typeof(TemplateEngine).Assembly;

    public string Render(string templateName, Dictionary<string, string> tokens)
    {
        var template = LoadTemplate(templateName);
        return tokens.Aggregate(template, (current, kv) =>
            current.Replace($"{{{{{kv.Key}}}}}", kv.Value));
    }

    private static string LoadTemplate(string templateName)
    {
        var resourceName = $"Gantry.Infrastructure.Templates.Resources.{templateName}";
        using var stream = Assembly.GetManifestResourceStream(resourceName)
            ?? throw new GantryException($"Template '{templateName}' not found as embedded resource '{resourceName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
