using System.Windows;
using Lertaro.Core;

using Lertaro.App.Services.Plugin;
using Lertaro.App.Helpers.Visuals;
namespace Lertaro.App.Services.Theme;

public class ThemeManager
{
    private static readonly Lazy<ThemeManager> _instance = new(() => new ThemeManager());
    public static ThemeManager Instance => _instance.Value;

    private ResourceDictionary? _activeThemeDictionary;
    private string _currentThemeId = "Light";
    private PluginSdk.Abstractions.ITheme? _activeTheme;

    public string CurrentThemeId => _currentThemeId;
    public ResourceDictionary? ActiveThemeDictionary => _activeThemeDictionary;
    public PluginSdk.Abstractions.ITheme? ActiveTheme => _activeTheme;

    public event Action? ThemeChanged;

    private ThemeManager() => PluginSdk.Services.ThemeService.IsDarkThemeFunc = () => _activeTheme?.IsDark ?? false;

    public IEnumerable<PluginSdk.Abstractions.ITheme> GetAvailableThemes() => PluginManager.Instance.ThemeProviders
            .SelectMany(p => p.GetThemes())
            .GroupBy(t => t.Id)
            .Select(g => g.First()); // Avoid duplicates

    public void Initialize(string preferredThemeId) => ApplyTheme(preferredThemeId, saveSettings: false);

    /// <summary>Starts watching the OS light/dark setting and re-applies the user's configured
    /// light/dark theme pair whenever it flips, but only while ThemeFollowSystem is actually on --
    /// checked fresh off UserSettings each time rather than tracked locally, so toggling the setting
    /// off doesn't need a matching unsubscribe.</summary>
    public void InitializeSystemFollow()
    {
        SystemThemeWatcher.EnsureWatching();
        SystemThemeWatcher.SystemThemeChanged += () =>
        {
            // SystemEvents raises this on a non-UI thread. ApplyTheme touches WPF windows/dictionaries,
            // so marshal the whole handler body onto the Dispatcher before doing any theme work.
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                var settings = UserSettings.Load();
                if (!settings.ThemeFollowSystem) return;
                ApplyTheme(ResolveLightDarkThemeId(SystemThemeWatcher.IsSystemLight, settings), saveSettings: false);
            }));
        };
    }

    /// <summary>Resolves the "follow system" light/dark theme pair's configured Id to one that
    /// actually exists (and is still the right light/dark flavor) among currently loaded theme
    /// providers. Themes come entirely from plugins (including the built-ins), so a hardcoded
    /// "Light"/"Dark" fallback isn't safe -- if the configured Id is unset, its provider got
    /// uninstalled/disabled, or it no longer matches the requested flavor, fall back to whatever
    /// theme of that flavor happens to be first in the available list instead.</summary>
    public string ResolveLightDarkThemeId(bool wantLight, UserSettings? settings = null)
    {
        settings ??= UserSettings.Load();
        var configured = wantLight ? settings.LightThemeId : settings.DarkThemeId;
        var themes = GetAvailableThemes().Where(t => t.IsDark != wantLight).ToList();
        if (!string.IsNullOrEmpty(configured) && themes.Any(t => string.Equals(t.Id, configured, StringComparison.OrdinalIgnoreCase)))
        {
            return configured;
        }
        return themes.FirstOrDefault()?.Id ?? "Light";
    }

    public bool ApplyTheme(string themeId, bool saveSettings = true)
    {
        var themes = GetAvailableThemes().ToList();
        // Fallback to Light if not found
        var theme = themes.FirstOrDefault(t => string.Equals(t.Id, themeId, StringComparison.OrdinalIgnoreCase)) ?? themes.FirstOrDefault(t => string.Equals(t.Id, "Light", StringComparison.OrdinalIgnoreCase));
        if (theme == null)
        {
            Logger.Log($"[ThemeManager] No themes found, failed to apply theme '{themeId}'", LogLevel.Error);
            return false;
        }

        _currentThemeId = theme.Id;
        _activeTheme = theme;

        try
        {
            var newDict = theme.GetResources();

            if (_activeThemeDictionary == null)
            {
                // Synchronous application for initial startup
                var appResources = System.Windows.Application.Current.Resources;
                appResources.MergedDictionaries.Add(newDict);
                _activeThemeDictionary = newDict;

                foreach (Window window in System.Windows.Application.Current.Windows)
                {
                    WindowEffectHelper.ApplyThemeEffects(window, theme);
                }
                ThemeChanged?.Invoke();
            }
            else
            {
                // Fade-out transition
                foreach (Window window in System.Windows.Application.Current.Windows)
                {
                    if (window.Content is UIElement content)
                    {
                        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(0.1, TimeSpan.FromMilliseconds(120));
                        content.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                    }
                }

                // Swap dictionaries and Fade-in transition
                Task.Run(async () =>
                {
                    await Task.Delay(120);
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        var appResources = System.Windows.Application.Current.Resources;
                        if (_activeThemeDictionary != null)
                        {
                            appResources.MergedDictionaries.Remove(_activeThemeDictionary);
                        }

                        appResources.MergedDictionaries.Add(newDict);
                        _activeThemeDictionary = newDict;

                        foreach (Window window in System.Windows.Application.Current.Windows)
                        {
                            WindowEffectHelper.ApplyThemeEffects(window, theme);
                            if (window.Content is UIElement content)
                            {
                                var targetOpacity = theme.WindowOpacity;
                                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(targetOpacity, TimeSpan.FromMilliseconds(180));
                                content.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                            }
                        }
                        ThemeChanged?.Invoke();
                    });
                });
            }

            Logger.Log($"[ThemeManager] Theme applied successfully: '{theme.DisplayName}' (Dark: {theme.IsDark})");

            if (saveSettings)
            {
                var settings = UserSettings.Load();
                settings.Theme = _currentThemeId;
                settings.Save();
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[ThemeManager] Error applying theme '{themeId}': {ex.Message}", LogLevel.Error);
            return false;
        }
    }
}
