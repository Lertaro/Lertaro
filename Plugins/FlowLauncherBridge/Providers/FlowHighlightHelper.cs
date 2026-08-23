using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Providers;

/// <summary>
/// Computes highlight masks for Flow plugin search results, stripping trigger keywords and subcommands.
/// </summary>
public static class FlowHighlightHelper
{
    public static bool[]? GetHighlightMask(FlowPluginHost host, string triggerKeyword, string text, string query)
    {
        if (string.IsNullOrEmpty(query))
            return null;

        var trimmed = query.Trim();
        var kw = string.IsNullOrWhiteSpace(triggerKeyword) ? "flow" : triggerKeyword;

        if (trimmed.Equals(kw, StringComparison.OrdinalIgnoreCase))
            return new bool[text.Length];

        if (trimmed.StartsWith(kw + " ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = trimmed[(kw.Length + 1)..].TrimStart();
            foreach (var sub in new[] { "install", "update", "uninstall" })
            {
                if (rest.Equals(sub, StringComparison.OrdinalIgnoreCase))
                    return new bool[text.Length];

                if (rest.StartsWith(sub + " ", StringComparison.OrdinalIgnoreCase))
                {
                    var term = rest[(sub.Length + 1)..].Trim();
                    return ComputeMask(text, term);
                }
            }

            return ComputeMask(text, rest);
        }

        foreach (var (actionKw, _) in host.KeywordPlugins)
        {
            if (trimmed.StartsWith(actionKw + " ", StringComparison.OrdinalIgnoreCase))
            {
                var term = trimmed[(actionKw.Length + 1)..].Trim();
                return ComputeMask(text, term);
            }
        }

        return ComputeMask(text, trimmed);
    }

    private static bool[] ComputeMask(string text, string searchTerm)
    {
        var mask = new bool[text.Length];
        if (string.IsNullOrWhiteSpace(searchTerm))
            return mask;

        if (FuzzyMatchService.GetHighlightMaskFunc != null)
        {
            var computed = FuzzyMatchService.GetHighlightMask(text, searchTerm);
            if (computed != null && computed.Length == text.Length)
                return computed;
        }

        var idx = text.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            for (var i = 0; i < searchTerm.Length && idx + i < text.Length; i++)
                mask[idx + i] = true;
        }

        return mask;
    }
}
