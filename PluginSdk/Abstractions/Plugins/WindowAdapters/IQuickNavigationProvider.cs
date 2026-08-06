namespace Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

public enum MouseTriggerType
{
    DoubleClick,
    MiddleClick
}

/// <summary>
/// Defines a provider that supplies items for the Quick Navigation menu. Purely a content source --
/// whether the popup should open at all for a given click is a separate concern, decided by
/// <see cref="IQuickNavigationTriggerGate"/> (or, for file dialogs, <see cref="IFileDialogAdapter.CanShowQuickNav"/>).
/// Most plugins only need to implement this interface.
/// </summary>
public interface IQuickNavigationProvider : IPluginComponent
{
    /// <summary>
    /// The group name shown as a section header above this provider's own root-level items, so a user
    /// with more than one quick-navigation provider active can tell which contributed which entries.
    /// </summary>
    string GroupName { get; }

    /// <summary>The display name of the provider.</summary>
    string IPluginComponent.Name => GroupName;

    /// <summary>
    /// Optional action shown as a small button in this provider's own root-level group header (see
    /// <see cref="GroupName"/>) -- e.g. "add the current folder" for a bookmarking-style provider.
    /// Null (the default) means no button is shown. Invoked with the same ISearchResult GetMenuItems
    /// itself receives for the root level. A submenu-level equivalent exists too: see
    /// <see cref="DynamicMenuItem.IsHeader"/> and its own OnExecute for headers a provider returns
    /// from GetMenuItems directly, at any depth.
    /// </summary>
    Action<ISearchResult>? HeaderAction => null;

    /// <summary>Tooltip for <see cref="HeaderAction"/>'s button. Ignored if HeaderAction is null.</summary>
    string? HeaderActionTooltip => null;

    /// <summary>
    /// Determines whether this provider can supply navigation items for the given search result.
    /// </summary>
    bool CanProvide(ISearchResult result);

    /// <summary>
    /// Gets menu items for the navigation node or submenu handle.
    /// </summary>
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);

    /// <summary>
    /// Executes the command associated with the command ID.
    /// </summary>
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);

    /// <summary>
    /// Clears any cached handles or states.
    /// </summary>
    void ClearSession();
}
