using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;

using Lertaro.App.Services.Theme;
namespace Lertaro.App.ViewModels.Settings.General;

/// <summary>Theme selection, including the optional "follow system light/dark" mode. Split out of
/// GeneralSettingsViewModel to keep that file under the project's line limit. Every change here
/// applies live (no Apply()/staging), matching how the manual theme pick has always worked.</summary>
public class ThemeSettingsViewModel : ViewModelBase
{
    private readonly System.ComponentModel.PropertyChangedEventHandler _translationHandler;

    private readonly UserSettings _userSettings;
    private ThemeOption? _selectedTheme;
    private ThemeOption? _selectedLightTheme;
    private ThemeOption? _selectedDarkTheme;
    private bool _followSystem;
    private IReadOnlyList<ThemeOption>? _themeOptions;
    private IReadOnlyList<ThemeOption>? _lightThemeOptions;
    private IReadOnlyList<ThemeOption>? _darkThemeOptions;
    private IReadOnlyList<ThemeCardOption>? _themeCards;
    private ICommand? _selectThemeModeCommand;

    public ThemeSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;

        _selectedTheme = ThemeOptions.FirstOrDefault(o => o.Value == _userSettings.Theme)
                         ?? ThemeOptions.FirstOrDefault();
        _selectedLightTheme = LightThemeOptions.FirstOrDefault(o => o.Value == _userSettings.LightThemeId)
                              ?? LightThemeOptions.FirstOrDefault();
        _selectedDarkTheme = DarkThemeOptions.FirstOrDefault(o => o.Value == _userSettings.DarkThemeId)
                             ?? DarkThemeOptions.FirstOrDefault();
        _followSystem = _userSettings.ThemeFollowSystem;

        // Dynamically refresh properties when the language changes -- ThemeOption.Label is a
        // TranslationService lookup (Theme_<Id>), so it genuinely changes text, unlike the option's
        // Id/Value. See GeneralSettingsViewModel's identical LogLevel/Language handling for why this
        // needs an explicit re-match-by-Value after the ItemsSource rebuild rather than relying on
        // record value-equality to "just work".
        _translationHandler = (s, e) =>
        {
            _themeOptions = null;
            _lightThemeOptions = null;
            _darkThemeOptions = null;
            // ThemeCardOption.DisplayName is also a TranslationService lookup, so the card grid needs
            // the same invalidate-and-rebuild treatment as the combobox option lists above.
            _themeCards = null;
            OnPropertyChanged(nameof(ThemeOptions));
            OnPropertyChanged(nameof(LightThemeOptions));
            OnPropertyChanged(nameof(DarkThemeOptions));
            OnPropertyChanged(nameof(ThemeCards));
            OnPropertyChanged(nameof(LightThemeCards));
            OnPropertyChanged(nameof(DarkThemeCards));
            OnPropertyChanged(nameof(ManualThemeCards));

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                var newTheme = ThemeOptions.FirstOrDefault(o => o.Value == _userSettings.Theme);
                if (newTheme != null) SelectedTheme = newTheme;

                var newLightTheme = LightThemeOptions.FirstOrDefault(o => o.Value == _userSettings.LightThemeId);
                if (newLightTheme != null) SelectedLightTheme = newLightTheme;

                var newDarkTheme = DarkThemeOptions.FirstOrDefault(o => o.Value == _userSettings.DarkThemeId);
                if (newDarkTheme != null) SelectedDarkTheme = newDarkTheme;
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        };
        TranslationManager.Instance.PropertyChanged += _translationHandler;

    }

    public IReadOnlyList<ThemeOption> ThemeOptions => _themeOptions ??= SettingsOptionGenerator.GetThemeOptions();

    // Filtered to each half's own flavor -- a dark-flavored theme showing up as a candidate for the
    // "light" side (or vice versa) would defeat the point of "follow system" in the first place.
    public IReadOnlyList<ThemeOption> LightThemeOptions => _lightThemeOptions ??= SettingsOptionGenerator.GetThemeOptions(isDark: false);
    public IReadOnlyList<ThemeOption> DarkThemeOptions => _darkThemeOptions ??= SettingsOptionGenerator.GetThemeOptions(isDark: true);

    // Card-preview equivalents of the three option lists above, for the Appearance page's card grid.
    public IReadOnlyList<ThemeCardOption> ThemeCards => _themeCards ??= ThemeManager.Instance.GetAvailableThemes()
        .Select(t => new ThemeCardOption(t)).OrderBy(c => c.Id).ToList();
    public IReadOnlyList<ThemeCardOption> LightThemeCards => ThemeCards.Where(c => !c.IsDark).ToList();
    public IReadOnlyList<ThemeCardOption> DarkThemeCards => ThemeCards.Where(c => c.IsDark).ToList();

    // ThemeOption itself doesn't carry an IsDark flag (it's just Value/Label), so this checks
    // membership in DarkThemeOptions -- record value-equality (by Value) makes Contains reliable here.
    private bool IsSelectedThemeDark => _selectedTheme != null && DarkThemeOptions.Contains(_selectedTheme);

    // What the single manual-mode grid shows: whichever flavor the currently active theme is, so
    // picking "Light"/"Dark" mode (see SelectThemeModeCommand) narrows the grid instead of mixing
    // both flavors into one long scroll like the very first version of this page did.
    public IReadOnlyList<ThemeCardOption> ManualThemeCards => IsSelectedThemeDark ? DarkThemeCards : LightThemeCards;

    public string PreferredTheme => _userSettings.Theme;

    // Drives which of the three Theme Mode cards (Light / Dark / Follow System) shows as selected.
    public string ThemeModeTag => FollowSystem ? "FollowSystem" : IsSelectedThemeDark ? "Dark" : "Light";

    // Backs the three Theme Mode cards. "Light"/"Dark" turn Follow System off and jump the active
    // theme to whichever light/dark theme was last picked -- SelectedLightTheme/SelectedDarkTheme are
    // kept in sync with every SelectedTheme change (see that setter), so this always reflects the most
    // recent pick for that flavor, whether it came from the manual grid, this command, or the Follow
    // System light/dark grids, and falls back to LightThemeOptions/DarkThemeOptions' own
    // FirstOrDefault() when nothing was ever picked (see the constructor).
    public ICommand SelectThemeModeCommand => _selectThemeModeCommand ??= new RelayCommand<string>(mode =>
    {
        switch (mode)
        {
            case "FollowSystem":
                FollowSystem = true;
                break;
            case "Dark":
                FollowSystem = false;
                if (SelectedDarkTheme != null) SelectedTheme = SelectedDarkTheme;
                break;
            default:
                FollowSystem = false;
                if (SelectedLightTheme != null) SelectedTheme = SelectedLightTheme;
                break;
        }

        // The grid(s) this mode switch just revealed may have gone from Collapsed to Visible in this
        // same operation (via IsManualThemeEnabled/FollowSystem's own Visibility bindings), so their
        // ListBoxes haven't necessarily generated item containers yet at the moment the SelectedThemeId/
        // SelectedLightThemeId/SelectedDarkThemeId notifications above fired -- WPF silently drops a
        // SelectedValue that doesn't match any container yet and never retries once containers do show
        // up. Re-raising once layout has settled (the same DispatcherPriority.Loaded pattern the
        // language-change handler in the constructor uses) makes the highlight catch up.
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            OnPropertyChanged(nameof(SelectedThemeId));
            OnPropertyChanged(nameof(SelectedLightThemeId));
            OnPropertyChanged(nameof(SelectedDarkThemeId));
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    });

    // String-keyed mirrors of SelectedTheme/SelectedLightTheme/SelectedDarkTheme so the card grid's
    // ListBox can two-way bind via SelectedValue/SelectedValuePath (ThemeOption is an immutable record
    // with no settable Value, so binding straight into ".Value" isn't an option).
    public string? SelectedThemeId
    {
        get => SelectedTheme?.Value;
        set => SelectedTheme = ThemeOptions.FirstOrDefault(o => o.Value == value) ?? SelectedTheme;
    }

    public string? SelectedLightThemeId
    {
        get => SelectedLightTheme?.Value;
        set => SelectedLightTheme = LightThemeOptions.FirstOrDefault(o => o.Value == value) ?? SelectedLightTheme;
    }

    public string? SelectedDarkThemeId
    {
        get => SelectedDarkTheme?.Value;
        set => SelectedDarkTheme = DarkThemeOptions.FirstOrDefault(o => o.Value == value) ?? SelectedDarkTheme;
    }

    // The manual theme picker only makes sense when "follow system" is off -- hidden (not just
    // greyed out) the rest of the time, since the light/dark pair takes over that role.
    public bool IsManualThemeEnabled => !_followSystem;

    public ThemeOption? SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value == null) return;
            if (_selectedTheme != value)
            {
                var isThemeIdChanged = _userSettings.Theme != value.Value;
                _selectedTheme = value;
                _userSettings.Theme = value.Value;
                _userSettings.Save();
                if (isThemeIdChanged)
                {
                    ThemeManager.Instance.ApplyTheme(value.Value, saveSettings: false);
                }

                // Keep the per-flavor memory in sync with whatever theme just became active, no matter
                // which path picked it (the manual filtered grid, a Light/Dark mode-card switch, or the
                // Follow System light/dark grids) -- otherwise switching Light -> Dark -> Light again
                // would revert to a stale remembered pick instead of the one just chosen manually.
                if (IsSelectedThemeDark) SelectedDarkTheme = value; else SelectedLightTheme = value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(PreferredTheme));
                OnPropertyChanged(nameof(SelectedThemeId));
                OnPropertyChanged(nameof(ManualThemeCards));
                OnPropertyChanged(nameof(ThemeModeTag));
            }
        }
    }

    // Which theme "follow system" applies for light/dark, plus the on/off switch itself. Turning it
    // on switches to the resolved light/dark pick; turning it off switches back to the manually
    // selected theme (SelectedTheme's own setter never got a chance to apply while follow-system was
    // overriding it) -- either way, skip the ApplyTheme call (and its fade animation) entirely when
    // the target is already the active theme.
    public bool FollowSystem
    {
        get => _followSystem;
        set
        {
            if (_followSystem != value)
            {
                _followSystem = value;
                _userSettings.ThemeFollowSystem = value;
                _userSettings.Save();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsManualThemeEnabled));
                OnPropertyChanged(nameof(ThemeModeTag));
                // SelectedTheme's own setter only fires nameof(ManualThemeCards)/nameof(SelectedThemeId)
                // when the underlying theme actually changes -- but switching Follow System off often
                // lands on a theme that's already active (whatever Follow System had already resolved),
                // so SelectedTheme's guard skips those notifications entirely. Re-raise them here,
                // unconditionally, so the manual grid's ItemsSource and its selected-card highlight both
                // stay in sync with the mode cards instead of showing stale content from before the switch.
                OnPropertyChanged(nameof(ManualThemeCards));
                OnPropertyChanged(nameof(SelectedThemeId));

                var targetThemeId = value
                    ? ThemeManager.Instance.ResolveLightDarkThemeId(SystemThemeWatcher.IsSystemLight, _userSettings)
                    : _userSettings.Theme;
                if (!string.Equals(targetThemeId, ThemeManager.Instance.CurrentThemeId, StringComparison.OrdinalIgnoreCase))
                {
                    ThemeManager.Instance.ApplyTheme(targetThemeId, saveSettings: false);
                }
            }
        }
    }

    public ThemeOption? SelectedLightTheme
    {
        get => _selectedLightTheme;
        set
        {
            if (value == null) return;
            if (_selectedLightTheme != value)
            {
                // ThemeOption is a record, so a language switch alone (re-translated Label, same Id)
                // already trips this inequality -- gate the actual re-apply on the Id, not the record,
                // or every language change would needlessly re-apply and fade the active theme.
                var isThemeIdChanged = _userSettings.LightThemeId != value.Value;
                _selectedLightTheme = value;
                _userSettings.LightThemeId = value.Value;
                _userSettings.Save();
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedLightThemeId));
                if (isThemeIdChanged && _followSystem && SystemThemeWatcher.IsSystemLight)
                {
                    ThemeManager.Instance.ApplyTheme(value.Value, saveSettings: false);
                }
            }
        }
    }

    public ThemeOption? SelectedDarkTheme
    {
        get => _selectedDarkTheme;
        set
        {
            if (value == null) return;
            if (_selectedDarkTheme != value)
            {
                var isThemeIdChanged = _userSettings.DarkThemeId != value.Value;
                _selectedDarkTheme = value;
                _userSettings.DarkThemeId = value.Value;
                _userSettings.Save();
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedDarkThemeId));
                if (isThemeIdChanged && _followSystem && !SystemThemeWatcher.IsSystemLight)
                {
                    ThemeManager.Instance.ApplyTheme(value.Value, saveSettings: false);
                }
            }
        }
    }

    public void Cleanup() => TranslationManager.Instance.PropertyChanged -= _translationHandler;
}
