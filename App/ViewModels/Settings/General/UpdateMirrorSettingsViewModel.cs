using Lertaro.App.Services;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings.General;

// Split out of GeneralSettingsViewModel to keep that file under the repo's per-file line limit; this owns
// the one cohesive "route GitHub release downloads through the user's own mirrors" feature. The rows sit
// with the two update switches on the System tab. GitLab has no mirror: it is the line a user reaches
// precisely when GitHub is the one they cannot talk to.
//
// Save() commits _userSettings.GitHubUpdateMirrors.
public class UpdateMirrorSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    private string _mirror1;
    private string _mirror2;
    private string _mirror3;

    public UpdateMirrorSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        var mirrors = UpdateMirrorSettings.Normalize(userSettings.GitHubUpdateMirrors);
        _mirror1 = mirrors.Count > 0 ? mirrors[0] : string.Empty;
        _mirror2 = mirrors.Count > 1 ? mirrors[1] : string.Empty;
        _mirror3 = mirrors.Count > 2 ? mirrors[2] : string.Empty;
    }

    public string Mirror1
    {
        get => _mirror1;
        set => SetProperty(ref _mirror1, Validate(value));
    }

    public string Mirror2
    {
        get => _mirror2;
        set => SetProperty(ref _mirror2, Validate(value));
    }

    public string Mirror3
    {
        get => _mirror3;
        set => SetProperty(ref _mirror3, Validate(value));
    }

    // Throwing is what puts the row into the settings window's validation-error count and gates Apply/OK,
    // the same mechanism the inline-card width boxes use. The boxes bind on LostFocus (the TextBox default)
    // rather than PropertyChanged so a half-typed "https://" is not flagged on every keystroke.
    private static string Validate(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        // An empty slot is "not used", which is how a user runs with one or two mirrors.
        if (text.Length == 0 || UpdateMirrorSettings.IsValid(text)) return text;
        throw new ArgumentOutOfRangeException(nameof(value), TranslationManager.Instance["General_GitHubMirrorInvalid"]);
    }

    // Normalize compacts the slots, so a blank first box does not leave a hole ahead of a filled second
    // one: the order of what was typed is the order it is tried in.
    public void Save() =>
        _userSettings.GitHubUpdateMirrors = UpdateMirrorSettings.Normalize([_mirror1, _mirror2, _mirror3]);
}