namespace Lertaro.App.Services.ShellMenu.ActionFlyout;

// Post-processing for the merged (static + dynamic) item list built by ActionMenuBuilder, split out
// (composition, not a partial class) to keep ActionMenuBuilder.cs under the project's line limit.
// ActionMenuBuilder itself keeps the public FinalizeItems entry point as a thin forwarder into this
// class's implementation, so every external caller and test keeps calling ActionMenuBuilder.FinalizeItems
// unchanged.
internal static class ActionMenuBuilderFinalizeExtensions
{
    // Dedupes by text and tidies separators. Runs on the merged (static + dynamic) list.
    internal static List<ActionMenuItem> FinalizeItems(List<ActionMenuItem> uiItems)
    {
        var uniqueItems = new List<ActionMenuItem>();
        foreach (var item in uiItems)
        {
            if (item.IsSeparator || item.IsSectionHeader)
            {
                uniqueItems.Add(item);
                continue;
            }

            var existing = uniqueItems.Find(x => !x.IsSeparator && !x.IsSectionHeader && x.Text.Equals(item.Text, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (item.HasSubMenu && !existing.HasSubMenu)
                {
                    uniqueItems.Remove(existing);
                    uniqueItems.Add(item);
                }
            }
            else
            {
                uniqueItems.Add(item);
            }
        }

        var finalItems = new List<ActionMenuItem>();
        for (var i = 0; i < uniqueItems.Count; i++)
        {
            var current = uniqueItems[i];
            if (current.IsSeparator)
            {
                if (finalItems.Count == 0) continue;
                if (finalItems[finalItems.Count - 1].IsSeparator || finalItems[finalItems.Count - 1].IsSectionHeader) continue;
                if (i == uniqueItems.Count - 1) continue;
            }
            finalItems.Add(current);
        }

        return ReorderRootSections(finalItems);
    }

    // Reorders contiguous sections (each starting at an IsSectionHeader item) according to the user's
    // saved ActionMenuGroupOrder, most-preferred first. A section whose SectionGroupId isn't listed yet
    // falls back to its current position -- relying on List.Sort/OrderBy being STABLE, so unlisted
    // sections keep their natural discovery order (built-in first, then dynamic providers by Priority)
    // relative to each other. Safe no-op for submenu-level lists, which have at most one trivial section
    // with an empty SectionGroupId.
    private static List<ActionMenuItem> ReorderRootSections(List<ActionMenuItem> items)
    {
        var headerIndexes = new List<int>();
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].IsSectionHeader)
                headerIndexes.Add(i);
        }

        if (headerIndexes.Count < 2)
            return items;

        var order = Core.UserSettings.Load().ActionMenuGroupOrder;

        var sections = new List<List<ActionMenuItem>>();
        for (var i = 0; i < headerIndexes.Count; i++)
        {
            var start = headerIndexes[i];
            var end = i + 1 < headerIndexes.Count ? headerIndexes[i + 1] : items.Count;
            sections.Add(items.GetRange(start, end - start));
        }

        var reordered = sections
            .OrderBy(section =>
            {
                var rank = order.IndexOf(section[0].SectionGroupId);
                return rank >= 0 ? rank : int.MaxValue;
            })
            .SelectMany(section => section)
            .ToList();

        return reordered;
    }
}
