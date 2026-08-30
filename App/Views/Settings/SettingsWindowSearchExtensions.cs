using System.Windows;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Settings;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.App.ViewModels.Settings.QuickPanel;
using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.App;

// Title-bar search box + results popup for SettingsWindow.
// Activation logic lives in SettingsWindowSearchActivationHelper.cs to stay under the 300-line per-file limit.
internal static class SettingsWindowSearchExtensions
{
    public static void CloseSearchPopup(this SettingsWindow window) => window.SearchResultsPopup.IsOpen = false;

    internal static string BuildBreadcrumb(SettingsSearchEntry entry)
    {
        var parts = new List<string> { TranslationManager.Instance[$"Settings_{entry.Section}"] };
        if (entry.TabLabelKey != null)
            parts.Add(TranslationManager.Instance[entry.TabLabelKey]);
        if (entry.SubTabLabelKey != null)
            parts.Add(TranslationManager.Instance[entry.SubTabLabelKey]);
        return string.Join(" › ", parts);
    }

    internal static List<SettingsSearchResultItem> BuildAllEntries(SettingsViewModel? vm, bool evaluateConditionalVisibility = true)
    {
        var results = new List<SettingsSearchResultItem>();
        foreach (var entry in SettingsSearchIndex.Entries)
        {
            if (entry.IsVisible != null && (!evaluateConditionalVisibility || vm == null || !entry.IsVisible(vm)))
                continue;

            results.Add(new SettingsSearchResultItem(TranslationManager.Instance[entry.LabelKey], BuildBreadcrumb(entry), entry.Section, entry.Activate, entry.TargetElementName));
        }

        var pluginsSectionLabel = TranslationManager.Instance["Settings_Plugins"];
        var plugins = vm?.Plugins.Plugins ?? (IEnumerable<PluginInfoViewModel>)PluginLoaderHelper.BuildPluginList(UserSettings.Load());
        foreach (var plugin in plugins)
        {
            var capturedPlugin = plugin;
            void RevealPlugin(SettingsViewModel settings)
            {
                settings.Plugins.SelectedPlugin = capturedPlugin;
                capturedPlugin.IsConfigTab = false;
            }

            results.Add(new SettingsSearchResultItem(plugin.Name, pluginsSectionLabel, "Plugins", RevealPlugin,
                Reveal: new SettingsSearchDynamicReveal("PluginsList", capturedPlugin)));

            foreach (var component in plugin.RawComponents)
            {
                results.Add(new SettingsSearchResultItem(component.DisplayName, $"{pluginsSectionLabel} › {plugin.Name}", "Plugins", RevealPlugin,
                    Reveal: new SettingsSearchDynamicReveal(string.Empty, component)));
            }

            var configTabLabel = TranslationManager.Instance["Common_Configure"];

            void AddConfigFields(IEnumerable<PluginConfigFieldViewModel> fields, string parentBreadcrumb, PluginConfigFieldViewModel? ownerGroup = null)
            {
                foreach (var field in fields)
                {
                    var currentGroup = field.IsGroup ? field : ownerGroup;
                    var label = field.Label;
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        var breadcrumb = string.IsNullOrWhiteSpace(field.GroupName) || field.IsGroup
                            ? parentBreadcrumb
                            : $"{parentBreadcrumb} › {field.GroupName}";

                        var capturedGroup = currentGroup;
                        void RevealPluginConfig(SettingsViewModel settings)
                        {
                            settings.Plugins.SelectedPlugin = capturedPlugin;
                            capturedPlugin.IsConfigTab = true;
                            if (capturedGroup != null && capturedPlugin.ConfigGroups.Contains(capturedGroup))
                            {
                                capturedPlugin.SelectedConfigGroup = capturedGroup;
                            }
                        }

                        results.Add(new SettingsSearchResultItem(label, breadcrumb, "Plugins", RevealPluginConfig,
                            Reveal: new SettingsSearchDynamicReveal(string.Empty, field)));
                    }

                    if (field.Children is { Count: > 0 })
                    {
                        AddConfigFields(field.Children, parentBreadcrumb, currentGroup);
                    }
                }
            }

            if (plugin.ConfigFields is { Count: > 0 })
            {
                AddConfigFields(plugin.ConfigFields, $"{pluginsSectionLabel} › {plugin.Name} › {configTabLabel}");
            }
        }

        var hotkeysSectionLabel = TranslationManager.Instance["Settings_Hotkeys"];
        var pluginActionsTabLabel = TranslationManager.Instance["Hotkeys_Tab_PluginActions"];
        var pluginActionGroups = vm?.Hotkeys.PluginActionGroups ?? (IEnumerable<PluginActionGroupViewModel>)HotkeyPluginActionGroupBuilder.Build(UserSettings.Load().Hotkeys.PluginActionHotkeys);
        foreach (var group in pluginActionGroups)
        {
            var capturedGroup = group;
            void SelectPluginActionsTab(SettingsViewModel v) => v.Hotkeys.SelectedTab = "PluginActions";

            results.Add(new SettingsSearchResultItem(group.PluginName, $"{hotkeysSectionLabel} › {pluginActionsTabLabel}", "Hotkeys", SelectPluginActionsTab,
                Reveal: new SettingsSearchDynamicReveal("PluginActionGroupsList", capturedGroup)));

            foreach (var action in group.Items)
            {
                var searchLabel = string.IsNullOrWhiteSpace(action.HotkeyValue)
                    ? action.DisplayName
                    : $"{action.DisplayName} ({action.HotkeyValue})";

                results.Add(new SettingsSearchResultItem(searchLabel, $"{hotkeysSectionLabel} › {pluginActionsTabLabel} › {group.PluginName}", "Hotkeys", SelectPluginActionsTab,
                    Reveal: new SettingsSearchDynamicReveal("PluginActionGroupsList", capturedGroup, action)));
            }
        }

        var quickPanelSectionLabel = TranslationManager.Instance["Settings_QuickPanel"];
        var quickPanelPluginTabsLabel = TranslationManager.Instance["QuickPanel_PluginTabs"];
        var quickPanelPluginTabs = vm?.QuickPanel.PluginTabs ?? QuickPanelPluginTabCatalog.Available(UserSettings.Load().QuickPanel);
        foreach (var pluginTab in quickPanelPluginTabs)
        {
            var capturedTab = pluginTab;
            void SelectPluginTabSection(SettingsViewModel v) => v.QuickPanel.SelectedSection = "PluginTabs";

            results.Add(new SettingsSearchResultItem(capturedTab.Name, $"{quickPanelSectionLabel} › {quickPanelPluginTabsLabel}", "QuickPanel", SelectPluginTabSection,
                Reveal: new SettingsSearchDynamicReveal("QuickPanelPluginTabsList", capturedTab)));
        }

        return results;
    }

    public static void OnSettingsSearchTextChanged(this SettingsWindow window)
    {
        var query = window.TxtSettingsSearch.Text?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            window.CloseSearchPopup();
            return;
        }

        var vm = window.DataContext as SettingsViewModel;
        var results = RankSearchResults(query, BuildAllEntries(vm));

        window.LstSearchResults.ItemsSource = results;
        window.LstSearchResults.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        window.TxtSearchNoResults.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        window.SearchResultsPopup.IsOpen = true;
        window.LstSearchResults.SelectedIndex = results.Count > 0 ? 0 : -1;
    }

    public static void OnSettingsSearchKeyDown(this SettingsWindow window, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (window.LstSearchResults.SelectedItem is SettingsSearchResultItem item)
                window.ActivateSearchResult(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Down && window.LstSearchResults.Items.Count > 0)
        {
            window.LstSearchResults.SelectedIndex = (window.LstSearchResults.SelectedIndex + 1) % window.LstSearchResults.Items.Count;
            window.ScrollSelectedResultIntoView();
            e.Handled = true;
        }
        else if (e.Key == Key.Up && window.LstSearchResults.Items.Count > 0)
        {
            var count = window.LstSearchResults.Items.Count;
            window.LstSearchResults.SelectedIndex = (window.LstSearchResults.SelectedIndex - 1 + count) % count;
            window.ScrollSelectedResultIntoView();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            window.TxtSettingsSearch.Text = string.Empty;
            e.Handled = true;
        }
    }

    private static void ScrollSelectedResultIntoView(this SettingsWindow window)
    {
        if (window.LstSearchResults.SelectedItem != null)
            window.LstSearchResults.ScrollIntoView(window.LstSearchResults.SelectedItem);
    }

    public static void OnSettingsSearchResultsMouseUp(this SettingsWindow window, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.OriginalSource is FrameworkElement { DataContext: SettingsSearchResultItem item })
            window.ActivateSearchResult(item);
    }

    internal static List<SettingsSearchResultItem> RankSearchResults(
        string query, IEnumerable<SettingsSearchResultItem> entries) => entries
            .Select((entry, index) => (entry, index))
            .Where(candidate => FuzzyMatcher.IsMatch(query, candidate.entry.Label))
            .Select(candidate => (
                candidate.entry,
                candidate.index,
                score: FuzzyMatcher.ComputeMatchWeight(candidate.entry.Label, query)))
            .OrderByDescending(candidate => candidate.score)
            .ThenBy(candidate => candidate.index)
            .Select(candidate => candidate.entry)
            .ToList();
}
