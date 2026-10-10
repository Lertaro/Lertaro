using Lertaro.App.ViewModels.Settings.General;
using Lertaro.App.ViewModels.Settings.Plugins;

namespace Lertaro.App.ViewModels.Settings;

/// <summary>
/// The Settings window's pre-save gate: gathers the errors the pages are showing and holds the reason shown
/// in the status bar when Apply refused because of them.
/// </summary>
/// <remarks>
/// Split out of SettingsViewModel to keep that file within the repository's per-file line limit; this class
/// has no state beyond the message it is showing and the two page lookups it is handed, and it always
/// operates on the one SettingsViewModel that owns it.
///
/// Why a page's own errors have to reach Apply at all: WPF's Validation.Error only fires for rules expressed
/// in a binding -- IDataErrorInfo, exception validation, converters -- so a rule a page works out for itself
/// (a trigger character the search syntax would consume, two plugins claiming the same prefix) would
/// otherwise never stop a save, and Apply would write a value the page was visibly reporting as broken.
///
/// Only pages already constructed are asked. An unvisited page holds no staged edit and so can report no
/// error, and going through a lazy property to ask would construct it (see the Plugins page) purely to be
/// told so.
///
/// The two members the window BINDS (the refusal message and its flag) are public on purpose: a binding
/// path resolves public members only, so internal ones are silently skipped -- see SearchViewHints' own
/// class comment for the full explanation. The type is public for the same reason plus C#'s own rule
/// against a public property of a less-accessible type (CS0053).
/// </remarks>
public sealed class SettingsValidationGate(
    GeneralSettingsViewModel general,
    Func<PluginManagementViewModel?> plugins) : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private string? _refusalMessage;

    /// <summary>Every blocking error the built pages are currently showing.</summary>
    internal IReadOnlyList<string> Errors
    {
        get
        {
            var errors = new List<string>(general.ValidationErrors);
            var pluginPage = plugins();
            if (pluginPage != null)
                errors.AddRange(pluginPage.ValidationErrors);
            return errors;
        }
    }

    /// <summary>
    /// Why the last Apply was refused, or null when there is nothing to report. The window's status bar shows
    /// this in place of the generic save tip.
    /// </summary>
    public string? RefusalMessage
    {
        get => _refusalMessage;
        private set
        {
            if (_refusalMessage == value)
                return;

            _refusalMessage = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(RefusalMessage)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HasRefusal)));
        }
    }

    /// <summary>True while the status bar is showing a refusal instead of the generic save tip.</summary>
    public bool HasRefusal => !string.IsNullOrEmpty(_refusalMessage);

    /// <summary>Records that Apply refused, and says so on screen.</summary>
    /// <remarks>
    /// The COUNT rather than the first message: a plugin-grown message carries its own "pluginId.key:"
    /// diagnostic prefix (see PluginConfigFieldValidationSupport.Describe), while the readable reason is the
    /// red text under the field itself -- and every blocking error has one, since blocking takes a staged
    /// edit on that very field. Reporting it here at all is what stops a refused Apply from looking like
    /// nothing happening: the button stays enabled by design (see SettingsViewModel.RefreshCanApply) and the
    /// field may be on a tab the user is not looking at.
    /// </remarks>
    internal void Refuse(int errorCount)
        => RefusalMessage = string.Format(Services.TranslationManager.Instance["Settings_ApplyRefused"], errorCount);

    /// <summary>Clears the refusal, which Apply does as soon as it is allowed to save again.</summary>
    internal void Clear() => RefusalMessage = null;
}
