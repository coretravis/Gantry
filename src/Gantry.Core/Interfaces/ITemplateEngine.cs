namespace Gantry.Core.Interfaces;

/// <summary>Renders embedded resource templates by replacing {{token}} placeholders.</summary>
public interface ITemplateEngine
{
    string Render(string templateName, Dictionary<string, string> tokens);
}
