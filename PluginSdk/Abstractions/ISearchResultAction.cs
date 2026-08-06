using System.Windows.Media;

namespace Lertaro.PluginSdk.Abstractions;

public enum SearchWindowType
{
    Main,
    Quick,
    Inline
}

/// <summary>
/// Represents an action that can be performed on a search result.
/// </summary>
public interface ISearchResultAction : Plugins.IPluginComponent
{

    /// <summary>The group name this action belongs to (for visual categorisation).</summary>
    string GroupName { get; }

    /// <summary>The display name shown in the Actions menu.</summary>
    string DisplayName { get; }

    /// <summary>The name of the component.</summary>
    string Plugins.IPluginComponent.Name => DisplayName;

    /// <summary>The keyboard shortcut/hotkey associated with this action (e.g. "Ctrl+Shift+C", "Alt+D").</summary>
    string Hotkey => string.Empty;

    /// <summary>Keywords that expose this action as a search result instead of an action-menu item.</summary>
    IReadOnlyList<string> Keywords => Array.Empty<string>();

    /// <summary>Parameter names used for displaying the search-result form of this action.</summary>
    IReadOnlyList<string> Parameters => Array.Empty<string>();

    /// <summary>Determines whether this action is visible in the search result matching for the given window type.</summary>
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => true;

    /// <summary>Determines whether this action should be visible in the right-click / action menu.</summary>
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => Keywords.Count == 0;

    /// <summary>The icon associated with this action.</summary>
    ImageSource? Icon { get; }

    /// <summary>Determines if this action is applicable to the given (one or more) search results.</summary>
    bool CanExecute(IReadOnlyList<ISearchResult> results);

    /// <summary>Executes the action on the given (one or more) search results.</summary>
    void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view);
}
