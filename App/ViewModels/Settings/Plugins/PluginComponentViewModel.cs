namespace Lertaro.App.ViewModels.Settings.Plugins;

/// <summary>
/// Represents a single sub-component of a plugin (action, provider, etc.) that can be enabled/disabled.
/// Split out of PluginInfoViewModel to give the standalone component view model its own file.
/// </summary>
public class PluginComponentViewModel : ViewModelBase
{
    private bool _isEnabled;

    public PluginComponentViewModel(string componentId, PluginComponentType componentType, string displayName, bool isEnabled, string description = "")
    {
        ComponentId = componentId;
        ComponentType = componentType;
        DisplayName = displayName;
        _isEnabled = isEnabled;
        Description = description;
    }

    /// <summary>The stable unique ID used to persist the disabled state.</summary>
    public string ComponentId { get; }

    /// <summary>The category/type of this component (strongly-typed enum).</summary>
    public PluginComponentType ComponentType { get; }

    public string DisplayName { get; }

    public string Description { get; }

    /// <summary>
    /// Whether the user can toggle this component on/off.
    /// TranslationProvider and ThemeProvider components are shown read-only and cannot be disabled.
    /// </summary>
    public bool IsToggleable => ComponentType != PluginComponentType.TranslationProvider && ComponentType != PluginComponentType.ThemeProvider;

    /// <summary>Set once the user actually flips this checkbox. Lets Save() apply only components the
    /// user touched in this page, instead of blindly re-asserting this snapshot's IsEnabled for every
    /// component -- which would clobber changes made through other channels (e.g. closing a Startup
    /// Panel tab's x button, or the Startup Panel settings page's own re-enable checkbox) in the same
    /// Settings window session.</summary>
    public bool IsDirty { get; private set; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
                IsDirty = true;
        }
    }
}
