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
}
