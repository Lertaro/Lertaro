using System.Windows;
using System.Windows.Controls;
using ListBox = System.Windows.Controls.ListBox;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Services.AppWindow;

/// <summary>
/// Shared interface between QuickSearchWindow and InlineSearchWindow
/// to decouple and share the ShellMenuPresenter context menu controller.
/// </summary>
public interface ISearchWindow : IPluginSearchWindow
{
    UIElement ResultsPanel { get; }
    ListBox LstResults { get; }
    Grid GridSearchResults { get; }
    Grid GridActions { get; }
    TextBlock TxtActionsTarget { get; }
    ListBox LstActions { get; }
    System.Windows.Controls.TextBox ActionsSearchTextBox { get; }
    string SearchText { get; }
    System.Windows.Controls.TextBox SearchTextBox { get; }
    bool UsesFloatingActionsMenu { get; }
    bool KeepWindowOpenAfterActionsHotkey { get; }
    bool IsInActionsMode { get; set; }
    void UpdateActionsLayout();
    void FocusSearch();

    /// <summary>
    /// Hides the window because a result was executed, as opposed to the user cancelling (Escape, an empty
    /// box, Explorer taking over). The inline card reports that difference to the hook as the executed flag
    /// of <c>InlineSearchFinished</c>; windows that do not report it simply hide.
    /// </summary>
    void HideWindowAfterExecution() => HideWindow();
}

// The hotkey trigger and PluginActionExecutor are typed against the plugin-facing interface, so they reach
// the same method through here instead of each deciding which windows report the execution. An instance
// method always wins over an extension method, so our own windows use the interface default above.
internal static class SearchWindowHideExtensions
{
    public static void HideWindowAfterExecution(this IPluginSearchWindow view)
    {
        if (view is ISearchWindow searchWindow) searchWindow.HideWindowAfterExecution();
        else view.HideWindow();
    }
}
