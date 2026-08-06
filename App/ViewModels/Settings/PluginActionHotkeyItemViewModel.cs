using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.ViewModels.Settings;

/// <summary>One rebindable row in the "Plugin Actions" tab -- a single ISearchResultAction.</summary>
public class PluginActionHotkeyItemViewModel : ViewModelBase
{
    private readonly ISearchResultAction _action;

    public string PluginId { get; }
    public string ActionId { get; }
    public string DefaultHotkey { get; }

    // Reads the action's own DisplayName live (rather than capturing it once) so it can be
    // refreshed on a runtime language switch via RefreshDisplayName().
    public string DisplayName => _action.DisplayName;

    private string _hotkeyValue;
    public string HotkeyValue
    {
        get => _hotkeyValue;
        set => SetProperty(ref _hotkeyValue, value);
    }

    public PluginActionHotkeyItemViewModel(string pluginId, ISearchResultAction action, string currentValue)
    {
        PluginId = pluginId;
        _action = action;
        ActionId = action.GetType().Name;
        DefaultHotkey = action.Hotkey;
        _hotkeyValue = currentValue;
    }

    public void RefreshDisplayName() => OnPropertyChanged(nameof(DisplayName));
}

/// <summary>Groups the rebindable actions belonging to one plugin, for the divider-separated list.</summary>
public class PluginActionGroupViewModel : ViewModelBase
{
    private readonly IPlugin _plugin;

    public string PluginName => _plugin.Name;
    public List<PluginActionHotkeyItemViewModel> Items { get; }

    public PluginActionGroupViewModel(IPlugin plugin, List<PluginActionHotkeyItemViewModel> items)
    {
        _plugin = plugin;
        Items = items;
    }

    public void RefreshPluginName() => OnPropertyChanged(nameof(PluginName));
}
