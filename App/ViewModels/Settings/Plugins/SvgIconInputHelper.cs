using System.IO;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;

namespace Lertaro.App.ViewModels.Settings.Plugins;

/// <summary>
/// Converts the small SVG subset accepted by the icon settings fields into the WPF Path Data
/// already used by the application. This deliberately is not a general SVG renderer.
/// </summary>
internal static class SvgIconInputHelper
{
    private const long MaxDocumentCharacters = 1_000_000;

    public static bool LooksLikeSvgDocument(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return trimmed.StartsWith("<", StringComparison.Ordinal);
    }

    public static bool TryConvert(string value, out string pathData)
    {
        pathData = string.Empty;
        if (!LooksLikeSvgDocument(value))
            return false;

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxDocumentCharacters
            };

            using var reader = XmlReader.Create(new StringReader(value), settings);
            var document = XDocument.Load(reader, LoadOptions.None);
            var root = document.Root;
            if (root is null || !string.Equals(root.Name.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
                return false;

            var pathParts = root
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "path", StringComparison.OrdinalIgnoreCase))
                .Select(element => element.Attributes().FirstOrDefault(attribute =>
                    string.Equals(attribute.Name.LocalName, "d", StringComparison.OrdinalIgnoreCase))?.Value)
                .OfType<string>()
                .Select(NormalizeWhitespace)
                .Where(data => data.Length > 0)
                .ToList();

            if (pathParts.Count == 0)
                return false;

            var merged = string.Join(" ", pathParts);
            Geometry.Parse(merged);
            pathData = merged;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsValidPathData(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        try
        {
            Geometry.Parse(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeWhitespace(string value) =>
        string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
