using Microsoft.Win32;

namespace Lertaro.App.Services.Theme;

/// <summary>Reads and watches the Windows "apps use light theme" registry setting, independent of
/// Lertaro's own <see cref="ThemeManager"/> theme -- used to drive the optional "follow system"
/// mode. Static/process-wide since there's only ever one OS theme to watch.</summary>
public static class SystemThemeWatcher
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName = "AppsUseLightTheme";

    private static bool _isLight = ReadIsLight();
    private static bool _subscribed;

    public static bool IsSystemLight => _isLight;

    public static event Action? SystemThemeChanged;

    public static void EnsureWatching()
    {
        if (_subscribed) return;
        _subscribed = true;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // The light/dark toggle surfaces as a UserPreferenceCategory.General change alongside a lot of
        // unrelated ones (taskbar, colors, ...) -- re-read the registry value and only fire if it
        // actually flipped, since General fires far more often than the theme itself changes.
        if (e.Category != UserPreferenceCategory.General) return;

        var newValue = ReadIsLight();
        if (newValue == _isLight) return;
        _isLight = newValue;
        SystemThemeChanged?.Invoke();
    }

    private static bool ReadIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            // Missing key/value means an unmigrated or pre-personalization install -- Windows' own
            // default there is light.
            return key?.GetValue(ValueName) is not int value || value != 0;
        }
        catch
        {
            return true;
        }
    }
}
