namespace Lertaro.Core;

/// <summary>
/// The user's UI font fallback chain: up to three family names in priority order, ahead of the app's
/// own default. Lives in Core because settings persistence and the WPF typography service must agree
/// on what a valid chain is.
/// </summary>
public static class UiFontSettings
{
    public const int MaxFamilies = 3;

    /// <summary>
    /// Trims and de-duplicates case-insensitively, drops empty entries and names a composite font
    /// string cannot carry, and keeps at most <see cref="MaxFamilies"/> entries in order.
    /// </summary>
    public static List<string> Normalize(IEnumerable<string?>? families)
    {
        var result = new List<string>(MaxFamilies);
        if (families == null) return result;
        foreach (var family in families)
        {
            var name = family?.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            // A comma starts the next family in a WPF composite font string, so such a name would
            // silently become two entries; line breaks are equally meaningless in a family name.
            if (name.Contains(',') || name.IndexOfAny(['\r', '\n']) >= 0) continue;
            if (result.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            result.Add(name);
            if (result.Count == MaxFamilies) break;
        }
        return result;
    }
}
