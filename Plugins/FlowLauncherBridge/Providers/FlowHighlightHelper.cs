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

        // Whatever is left once the word and any subcommand come off is what to highlight, so a bare
        // "flow" or "flow install" leaves nothing and masks nothing -- which is what the hand-written
        // empty-mask returns used to say.
        return ComputeMask(text, ResolveTerm(host, triggerKeyword, query));
    }

    private static string ResolveTerm(FlowPluginHost host, string triggerKeyword, string query)
    {
        var word = TriggerWord.Normalize(triggerKeyword);
        if (word.Length == 0)
            word = "flow";

        if (TriggerWord.TryMatch(query, word, out var rest))
            return SubcommandTerm(rest);

        // A Flow plugin's own action keyword ("gh lertaro") is the other prefix that reaches here.
        foreach (var (actionKeyword, _) in host.KeywordPlugins)
            if (TriggerWord.TryMatch(query, actionKeyword, out var actionTerm))
                return actionTerm;

        // Nothing owns the prefix -- and on the quick-window path the host already stripped the word it
        // did own, so what the row carries IS the term.
        return query.Trim();
    }

    // "install lertaro" is a search for "lertaro"; "install" alone has nothing to highlight.
    private static string SubcommandTerm(string rest)
    {
        foreach (var sub in new[] { "install", "update", "uninstall" })
            if (TriggerWord.TryMatch(rest, sub, out var term))
                return term;

        return rest;
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
