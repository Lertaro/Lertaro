using Lertaro.Plugins.CoreExtensions.Models;

namespace Lertaro.Plugins.CoreExtensions.Providers.QueryTokens;

/// <summary>
/// Expands references between custom filter rules before the shared wildcard parser evaluates them.
/// </summary>
internal static class CustomFilterRuleResolver
{
    public static string Expand(
        string? rule,
        IReadOnlyList<CustomFilterItem> filters,
        string prefix,
        bool allowDisabledReferences = false)
    {
        var expanded = new List<string>();
        var output = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ExpandRule(rule, filters, prefix, allowDisabledReferences, new HashSet<string>(StringComparer.OrdinalIgnoreCase), expanded, output);
        return string.Join("; ", expanded);
    }

    private static void ExpandRule(
        string? rule,
        IReadOnlyList<CustomFilterItem> filters,
        string prefix,
        bool allowDisabledReferences,
        HashSet<string> activeNames,
        List<string> expanded,
        HashSet<string> output)
    {
        if (string.IsNullOrWhiteSpace(rule))
            return;

        foreach (var token in rule.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || token.Length <= prefix.Length)
            {
                if (output.Add(token))
                    expanded.Add(token);
                continue;
            }

            var name = token[prefix.Length..].Trim();
            var referenced = filters.FirstOrDefault(filter =>
                string.Equals(filter.Keyword?.Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (referenced == null || (!allowDisabledReferences && !referenced.Enabled) || string.IsNullOrWhiteSpace(referenced.Keyword) || !activeNames.Add(name))
                continue;

            ExpandRule(referenced.Rule, filters, prefix, allowDisabledReferences, activeNames, expanded, output);
            activeNames.Remove(name);
        }
    }
}
