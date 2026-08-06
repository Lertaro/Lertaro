namespace Lertaro.Core;

/// <summary>
/// Turns "which sources exist right now" plus a tab's stored order and hidden list into the groups the
/// panel actually shows, in the order it shows them. Pure, and deliberately separate from
/// <see cref="QuickPanelSettings"/>: the settings only record what the user chose, while the panel has
/// to reconcile that against sources that appear and disappear between sessions (a folder added, a
/// plugin enabled, a source removed while its id is still listed).
/// </summary>
public static class QuickPanelGroupOrdering
{
    /// <summary>
    /// Visible group ids, most-preferred first. Ids in <paramref name="order"/> lead, in that order;
    /// anything available but unlisted keeps its discovery position after them, which is what makes a
    /// newly added source appear at the end instead of silently at the top. Ids in the order list that
    /// no longer exist are ignored rather than pruned -- a plugin that is only temporarily disabled
    /// should come back where the user put it, not at the end.
    /// </summary>
    public static List<string> Resolve(
        IEnumerable<string> available,
        IEnumerable<string>? order,
        IEnumerable<string>? disabled)
    {
        var hidden = new HashSet<string>(disabled ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var visible = available
            .Where(id => !string.IsNullOrEmpty(id) && !hidden.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ranked = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var rank = 0;
        foreach (var id in order ?? Enumerable.Empty<string>())
        {
            if (!string.IsNullOrEmpty(id) && !ranked.ContainsKey(id))
                ranked[id] = rank++;
        }

        // OrderBy is stable, so everything unlisted keeps the order `available` handed it over in.
        return visible.OrderBy(id => ranked.TryGetValue(id, out var r) ? r : int.MaxValue).ToList();
    }
}
