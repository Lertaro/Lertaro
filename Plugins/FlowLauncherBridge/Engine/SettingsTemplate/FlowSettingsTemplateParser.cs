using System.IO;
using System.Text.Json;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.SettingsTemplate;

/// <summary>
/// Parser for Flow.Launcher SettingsTemplate.yaml and SettingsTemplate.json files.
/// </summary>
public static class FlowSettingsTemplateParser
{
    public static FlowSettingsTemplateDoc ParseFile(string filePath)
    {
        if (!File.Exists(filePath))
            return new FlowSettingsTemplateDoc();

        var content = File.ReadAllText(filePath);
        return ParseContent(content, filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
    }

    public static FlowSettingsTemplateDoc ParseContent(string content, bool isJson)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new FlowSettingsTemplateDoc();

        if (isJson)
            return ParseJson(content);

        return ParseYaml(content);
    }

    private static FlowSettingsTemplateDoc ParseJson(string json)
    {
        var doc = new FlowSettingsTemplateDoc();
        try
        {
            using var jDoc = JsonDocument.Parse(json);
            if (jDoc.RootElement.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in body.EnumerateArray())
                {
                    var elem = new FlowSettingsTemplateElement();
                    if (item.TryGetProperty("type", out var typeProp))
                        elem.Type = typeProp.GetString() ?? string.Empty;

                    if (item.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in attrs.EnumerateObject())
                        {
                            if (prop.Value.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var opt in prop.Value.EnumerateArray())
                                {
                                    var str = opt.GetString();
                                    if (str != null) elem.Options.Add(str);
                                }
                            }
                            else
                            {
                                elem.Attributes[prop.Name] = prop.Value.ToString();
                            }
                        }
                    }
                    doc.Elements.Add(elem);
                }
            }
        }
        catch { }
        return doc;
    }

    private static FlowSettingsTemplateDoc ParseYaml(string yaml)
    {
        var doc = new FlowSettingsTemplateDoc();
        var lines = yaml.Replace("\r\n", "\n").Split('\n');

        FlowSettingsTemplateElement? currentElem = null;
        string? multilineKey = null;
        var multilineBuffer = new System.Text.StringBuilder();

        void FlushMultiline()
        {
            if (currentElem != null && multilineKey != null && multilineBuffer.Length > 0)
            {
                currentElem.Attributes[multilineKey] = multilineBuffer.ToString().Trim();
                multilineBuffer.Clear();
                multilineKey = null;
            }
        }

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (trimmed.StartsWith('#') || string.IsNullOrEmpty(trimmed))
            {
                if (multilineKey != null) multilineBuffer.AppendLine();
                continue;
            }

            var indent = rawLine.Length - rawLine.TrimStart().Length;

            if (multilineKey != null)
            {
                if (indent >= 4 && !trimmed.StartsWith('-') && !trimmed.Contains(':'))
                {
                    if (multilineBuffer.Length > 0) multilineBuffer.Append(' ');
                    multilineBuffer.Append(trimmed);
                    continue;
                }
                FlushMultiline();
            }

            if (trimmed.StartsWith("- type:") || (trimmed.StartsWith("type:") && indent <= 4))
            {
                FlushMultiline();
                currentElem = new FlowSettingsTemplateElement();
                doc.Elements.Add(currentElem);

                var typeVal = trimmed.StartsWith("- type:")
                    ? trimmed["- type:".Length..].Trim()
                    : trimmed["type:".Length..].Trim();
                currentElem.Type = CleanQuotes(typeVal);
                continue;
            }

            if (currentElem == null || trimmed == "attributes:" || trimmed == "body:")
                continue;

            if (trimmed.StartsWith("- ") && currentElem != null)
            {
                var opt = CleanQuotes(trimmed[2..].Trim());
                if (!string.IsNullOrEmpty(opt)) currentElem.Options.Add(opt);
                continue;
            }

            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx > 0)
            {
                var key = trimmed[..colonIdx].Trim();
                var val = trimmed[(colonIdx + 1)..].Trim();

                if (val == ">" || val == "|")
                {
                    multilineKey = key;
                    multilineBuffer.Clear();
                    continue;
                }

                currentElem?.Attributes[key] = CleanQuotes(val);
            }
        }

        FlushMultiline();
        return doc;
    }

    private static string CleanQuotes(string str)
    {
        if (string.IsNullOrEmpty(str)) return string.Empty;
        if (str.StartsWith('"') && str.EndsWith('"'))
        {
            if (str.Length >= 2)
            {
                var unquoted = str[1..^1];
                return unquoted.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t").Replace("\\\"", "\"");
            }
        }
        if (str.StartsWith('\'') && str.EndsWith('\''))
        {
            if (str.Length >= 2) return str[1..^1];
        }
        return str;
    }
}
