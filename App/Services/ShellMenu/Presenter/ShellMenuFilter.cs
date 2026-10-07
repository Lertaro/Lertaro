using Lertaro.Core.SearchIndex;
namespace Lertaro.App.Services.ShellMenu.Presenter;

public static class ShellMenuFilter
{
    internal static void ApplyToList(System.Windows.Controls.ListBox list, List<ActionMenuItem> rawItems, string filter, bool preserveSelection = false)
    {
        var selected = preserveSelection ? list.SelectedItem as ActionMenuItem : null;
        var cleanItems = Apply(rawItems, filter);
        foreach (var item in cleanItems) item.SearchQuery = filter;

        // A late dynamic-menu merge rebuilds static items and may reorder sections. Restore by
        // command identity, not object identity or row index; zero is not a command identity.
        var selectedIndex = selected == null ? -1 : cleanItems.FindIndex(item =>
            !item.IsSeparator && !item.IsSectionHeader && !item.IsDisabled
            && (ReferenceEquals(item, selected) || (selected.CommandId != 0 && item.CommandId == selected.CommandId)));
        list.ItemsSource = cleanItems;
        list.SelectedIndex = selectedIndex >= 0 ? selectedIndex
            : cleanItems.FindIndex(i => !i.IsSeparator && !i.IsSectionHeader && !i.IsDisabled);
        if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem);
    }

    public static List<ActionMenuItem> Apply(List<ActionMenuItem> rawItems, string filter)
    {
        var filtered = rawItems;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            // The standard match contract (FuzzyMatcher.IsMatch): same FzfPattern.Parse-based
            // multi-keyword splitting, fuzzy subsequence matching, and pinyin/alias fallback (with
            // proper quality gating) as every other search surface in the app, instead of a hand-
            // rolled two-pointer sequence match + raw alias substring check duplicating that logic.
            filtered = rawItems.Where(item =>
            {
                if (item.IsSectionHeader || item.IsSeparator)
                    return true;

                return !string.IsNullOrEmpty(item.Text) && FuzzyMatcher.IsMatch(filter, item.Text);
            }).ToList();
        }

        // Clean up consecutive separators or headers without items
        var cleanItems = new List<ActionMenuItem>();
        for (var i = 0; i < filtered.Count; i++)
        {
            var current = filtered[i];
            if (current.IsSeparator)
            {
                if (cleanItems.Count > 0 && !cleanItems[^1].IsSeparator && !cleanItems[^1].IsSectionHeader)
                {
                    cleanItems.Add(current);
                }
            }
            else if (current.IsSectionHeader)
            {
                var hasItems = false;
                for (var j = i + 1; j < filtered.Count; j++)
                {
                    if (filtered[j].IsSectionHeader) break;
                    if (!filtered[j].IsSeparator && !filtered[j].IsDisabled && !filtered[j].IsSectionHeader)
                    {
                        hasItems = true;
                        break;
                    }
                }
                if (hasItems) cleanItems.Add(current);
            }
            else
            {
                cleanItems.Add(current);
            }
        }

        while (cleanItems.Count > 0 && (cleanItems[^1].IsSeparator || cleanItems[^1].IsSectionHeader))
        {
            cleanItems.RemoveAt(cleanItems.Count - 1);
        }

        while (cleanItems.Count > 0 && cleanItems[0].IsSeparator)
        {
            cleanItems.RemoveAt(0);
        }

        // Return empty list if no actual items were matched
        if (cleanItems.Count == 0 || cleanItems.All(x => x.IsSeparator || x.IsSectionHeader))
        {
            return new List<ActionMenuItem>();
        }

        return cleanItems;
    }
}
