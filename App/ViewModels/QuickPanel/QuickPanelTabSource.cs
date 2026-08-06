using Lertaro.App.Services;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.ViewModels.QuickPanel;

/// <summary>One entry in the panel's strip, whatever kind of thing is behind it.</summary>
/// <remarks>
/// Two kinds, and the strip is deliberately blind to which is which: a workspace the user assembled out
/// of folders, and a whole list a plugin offers. They share an id space, an order and the number keys,
/// so anything that had to ask which kind it was holding would be a rule the user has to be told.
/// </remarks>
internal interface IQuickPanelTabSource
{
    string Id { get; }

    string Name { get; }

    /// <summary>The groups this tab shows, each filed as it lands rather than all at the end.</summary>
    /// <remarks>
    /// A workspace's groups arrive independently -- that is the whole point of the streaming load -- so
    /// the tab hands each one over as it has it instead of returning a finished list. A plugin tab has
    /// exactly one and calls this once.
    /// </remarks>
    Task LoadAsync(Action<QuickPanelGroupViewModel, int> place, CancellationToken token);

    /// <summary>Takes this tab out of the strip for good, by writing that down.</summary>
    void Close(QuickPanelSettings settings);
}

/// <summary>A workspace: the folders the user put in it, each its own group.</summary>
internal sealed class WorkspaceTabSource : IQuickPanelTabSource
{
    private readonly QuickPanelViewModel _panel;

    public WorkspaceTabSource(QuickPanelViewModel panel, QuickPanelTab workspace)
    {
        _panel = panel;
        Workspace = workspace;
    }

    public QuickPanelTab Workspace { get; }

    public string Id => Workspace.Id;

    public string Name => string.IsNullOrWhiteSpace(Workspace.Name)
        ? TranslationManager.Instance["QuickPanel_DefaultTabName"]
        : Workspace.Name.Trim();

    public Task LoadAsync(Action<QuickPanelGroupViewModel, int> place, CancellationToken token)
        => _panel.LoadWorkspaceAsync(Workspace, place, token);

    // Kept, not deleted: the source list this workspace holds is worth more than the tab, and rebuilding
    // it to get the tab back is the thing this avoids.
    public void Close(QuickPanelSettings settings)
    {
        var stored = settings.Tabs.FirstOrDefault(tab => tab.Id.Equals(Id, StringComparison.OrdinalIgnoreCase));
        stored?.Enabled = false;
    }
}

/// <summary>A plugin's own list, shown as a tab of its own rather than inside somebody's workspace.</summary>
internal sealed class PluginTabSource : IQuickPanelTabSource
{
    private readonly QuickPanelViewModel _panel;
    private readonly IQuickPanelTabProvider _provider;

    public PluginTabSource(QuickPanelViewModel panel, IQuickPanelTabProvider provider)
    {
        _panel = panel;
        _provider = provider;
    }

    public string Id => QuickPanelPluginTabs.ComponentId(_provider);

    public string Name => _provider.Name;

    public Task LoadAsync(Action<QuickPanelGroupViewModel, int> place, CancellationToken token)
        => _panel.LoadPluginTabAsync(_provider, Id, Name, place, token);

    // Closed rather than disabled: switching the component off under Settings > Plugins stops it being
    // loaded at all, which is a statement about the plugin. This one is only about the strip.
    public void Close(QuickPanelSettings settings)
    {
        if (!settings.ClosedPluginTabIds.Contains(Id, StringComparer.OrdinalIgnoreCase))
            settings.ClosedPluginTabIds.Add(Id);
    }
}
