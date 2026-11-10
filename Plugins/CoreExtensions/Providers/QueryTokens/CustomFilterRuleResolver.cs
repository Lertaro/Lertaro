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
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, CustomFilterItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var filter in filters)
            if (!string.IsNullOrWhiteSpace(filter.Keyword)) byName.TryAdd(filter.Keyword.Trim(), filter);

        // Each named rule contributes the same union on every visit. Visit it once and use an explicit
        // stack so shared references cannot expand exponentially and long chains cannot overflow the stack.
        var pending = new Stack<string>();
        PushTokens(rule);
        while (pending.TryPop(out var token))
        {
            if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || token.Length <= prefix.Length)
            {
                if (output.Add(token))
                    expanded.Add(token);
                continue;
            }

            var name = token[prefix.Length..].Trim();
            if (!byName.TryGetValue(name, out var referenced)
                || (!allowDisabledReferences && !referenced.Enabled) || !visited.Add(name))
                continue;

            PushTokens(referenced.Rule);
        }

        return string.Join("; ", expanded);

        void PushTokens(string? source)
        {
            if (string.IsNullOrWhiteSpace(source)) return;
            var tokens = source.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = tokens.Length - 1; i >= 0; i--) pending.Push(tokens[i]);
        }
    }
}
