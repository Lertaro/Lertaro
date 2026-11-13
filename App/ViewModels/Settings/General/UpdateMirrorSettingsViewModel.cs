using Lertaro.App.Services;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings.General;

// Split out of GeneralSettingsViewModel to keep that file under the repo's per-file line limit; this owns
// the one cohesive "route GitHub release downloads through the user's own mirrors" feature. The rows sit
// with the two update switches on the System tab. GitLab has no mirror: it is the line a user reaches
// precisely when GitHub is the one they cannot talk to, and it is asked with the user's own access token
// instead, which is the other half of what this ViewModel owns.
//
// Save() commits _userSettings.GitHubUpdateMirrors and _userSettings.GitLabUpdateToken.
public class UpdateMirrorSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    private string _mirror1;
    private string _mirror2;
    private string _mirror3;
    private string _gitLabToken;

    public UpdateMirrorSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        var mirrors = UpdateMirrorSettings.Normalize(userSettings.GitHubUpdateMirrors);
        _mirror1 = mirrors.Count > 0 ? mirrors[0] : string.Empty;
        _mirror2 = mirrors.Count > 1 ? mirrors[1] : string.Empty;
        _mirror3 = mirrors.Count > 2 ? mirrors[2] : string.Empty;
        _gitLabToken = UpdateMirrorSettings.NormalizeToken(userSettings.GitLabUpdateToken);
        Slots = [new(this, 1), new(this, 2), new(this, 3)];
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

    /// <summary>
    /// The three mirror boxes, in the order they are tried. Each box is bound to one of these rather than
    /// to <see cref="Mirror1"/> directly because the box that grows to the row while it is being edited
    /// takes its editor's bindings from its own DataContext, which has to be the one slot it edits.
    /// An array rather than a list so the XAML's <c>Slots[0]</c> path binds through a plain IList.
    /// </summary>
    public MirrorSlot[] Slots { get; }

    /// <summary>
    /// GitLab's personal access token. No validation: any non-blank value is handed to GitLab as typed,
    /// and a wrong one simply fails that line's request (the GitHub line still answers). Blank is what
    /// stops GitLab being asked at all, so it needs no separate switch.
    /// </summary>
    public string GitLabToken
    {
        get => _gitLabToken;
        set => SetProperty(ref _gitLabToken, value ?? string.Empty);
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
    // one: the order of what was typed is the order it is tried in. The token is trimmed for the same
    // reason persistence trims it -- a pasted one usually carries a trailing newline.
    public void Save()
    {
        _userSettings.GitHubUpdateMirrors = UpdateMirrorSettings.Normalize([_mirror1, _mirror2, _mirror3]);
        _userSettings.GitLabUpdateToken = UpdateMirrorSettings.NormalizeToken(_gitLabToken);
    }

    /// <summary>One mirror box's editable value, read and written through the row's own property.</summary>
    public sealed class MirrorSlot : ViewModelBase
    {
        private readonly UpdateMirrorSettingsViewModel _owner;
        private readonly int _number;

        public MirrorSlot(UpdateMirrorSettingsViewModel owner, int number)
        {
            _owner = owner;
            _number = number;
        }

        public string Value
        {
            get => _number switch { 1 => _owner._mirror1, 2 => _owner._mirror2, _ => _owner._mirror3 };
            // Assigning the owner's property is what validates: an unusable prefix throws here, exactly
            // as it would from the box bound to Mirror1 itself.
            set
            {
                switch (_number)
                {
                    case 1: _owner.Mirror1 = value; break;
                    case 2: _owner.Mirror2 = value; break;
                    default: _owner.Mirror3 = value; break;
                }
            }
        }
    }
}
