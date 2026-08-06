using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Lertaro.App.Helpers;
using Lertaro.App.ViewModels.Settings;

namespace Lertaro.App;

// Split out of SettingsWindowSearchExtensions.cs to keep that file under the repo's per-file line limit (300 lines).
// Contains search activation, visual tree element resolution, and section page mapping helpers.
internal static class SettingsWindowSearchActivationHelper
{
    // Internal: called by SettingsWindowSearchExtensions.OnSettingsSearchKeyDown / OnSettingsSearchResultsMouseUp
    // and SettingsWindow.JumpToEntry.
    internal static void ActivateSearchResult(this SettingsWindow window, SettingsSearchResultItem item)
    {
        if (window.DataContext is SettingsViewModel vm)
            item.Activate?.Invoke(vm);

        window.SelectSection(item.Section);
        // Clearing the text fires TxtSettingsSearch_TextChanged, which closes the popup.
        window.TxtSettingsSearch.Text = string.Empty;

        if (item.TargetElementName != null)
        {
            var targetName = item.TargetElementName;
            var section = item.Section;
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ResolveNamedElement(window.GetSectionPage(section), targetName) is FrameworkElement target)
                {
                    target.BringIntoView();
                    SettingsSearchHighlight.Show(target);
                }
            }), DispatcherPriority.ContextIdle);
        }
        else if (item.Reveal != null)
        {
            var reveal = item.Reveal;
            var section = item.Section;
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                var page = window.GetSectionPage(section);
                if (page == null) return;

                if (reveal.ListElementName.Length == 0)
                {
                    if (FindDescendantByDataContext(page, reveal.GroupItem) is not { } found) return;
                    found.BringIntoView();
                    SettingsSearchHighlight.Show(found);
                    return;
                }

                if (page.FindName(reveal.ListElementName) is not ItemsControl list
                    || list.ItemContainerGenerator.ContainerFromItem(reveal.GroupItem) is not FrameworkElement groupContainer)
                    return;

                var target = reveal.ChildItem != null ? FindDescendantByDataContext(groupContainer, reveal.ChildItem) : null;
                target ??= groupContainer;
                target.BringIntoView();
                SettingsSearchHighlight.Show(target);
            }), DispatcherPriority.ContextIdle);
        }
    }

    private static FrameworkElement? ResolveNamedElement(FrameworkElement? root, string path)
    {
        var current = root;
        foreach (var segment in path.Split('/'))
        {
            if (current?.FindName(segment) is not FrameworkElement next)
                return null;
            current = next;
        }
        return current;
    }

    private static FrameworkElement? FindDescendantByDataContext(DependencyObject root, object dataContext)
    {
        var childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { } fe && ReferenceEquals(fe.DataContext, dataContext))
                return fe;

            if (FindDescendantByDataContext(child, dataContext) is FrameworkElement found)
                return found;
        }
        return null;
    }

    internal static FrameworkElement? GetSectionPage(this SettingsWindow window, string section) => section switch
    {
        "Service" => window.PageService,
        "Index" => window.PageIndex,
        "General" => window.PageGeneral,
        "Appearance" => window.PageAppearance,
        "Hotkeys" => window.PageHotkeys,
        "Plugins" => window.PagePlugins,
        "History" => window.PageHistory,
        "Favorites" => window.PageFavorites,
        "QuickPanel" => window.PageQuickPanel,
        "LocalSend" => window.PageLocalSend,
        "About" => window.PageAbout,
        _ => null,
    };
}
