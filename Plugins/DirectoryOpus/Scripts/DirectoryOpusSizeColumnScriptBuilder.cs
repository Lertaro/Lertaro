using System.IO;
using System.Reflection;
using System.Globalization;
using System.Text;

namespace Lertaro.Plugins.DirectoryOpus.Scripts;

/// <summary>Renders the bundled Directory Opus script with the current localized metadata and CLI path.</summary>
internal static class DirectoryOpusSizeColumnScriptBuilder
{
    private const string ResourceName = "DirectoryOpus.Resources.Scripts.LertaroSize.js.template";

    public static string Build(Assembly assembly, string label, string description, string cliPath)
    {
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource '{ResourceName}'.");
        using var reader = new StreamReader(stream);
        return Render(reader.ReadToEnd(), label, description, cliPath);
    }

    internal static string Render(string template, string label, string description, string cliPath) => template
        .Replace("{{LABEL}}", EscapeJScriptString(label), StringComparison.Ordinal)
        .Replace("{{DESCRIPTION}}", EscapeJScriptString(description), StringComparison.Ordinal)
        .Replace("{{LFF_PATH}}", EscapeJScriptString(cliPath), StringComparison.Ordinal);

    private static string EscapeJScriptString(string value)
    {
        var escaped = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\': escaped.Append("\\\\"); break;
                case '\"': escaped.Append("\\\""); break;
                case '\r': escaped.Append("\\r"); break;
                case '\n': escaped.Append("\\n"); break;
                default:
                    if (character > 0x7f)
                        escaped.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    else
                        escaped.Append(character);
                    break;
            }
        }
        return escaped.ToString();
    }
}
