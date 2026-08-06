using Lertaro.App.Services;
using Lertaro.Core;

using Lertaro.App.Services.Theme;
namespace Lertaro.App.ViewModels.Settings.General;

public static class SettingsOptionGenerator
{
    public static IReadOnlyList<LogLevelOption> GetLogLevelOptions() => new[]
        {
            new LogLevelOption("Error", TranslationManager.Instance["LogLevel_Error"]),
            new LogLevelOption("Warn", TranslationManager.Instance["LogLevel_Warn"]),
            new LogLevelOption("Info", TranslationManager.Instance["LogLevel_Info"]),
            new LogLevelOption("Debug", TranslationManager.Instance["LogLevel_Debug"])
        };

    public static IReadOnlyList<LanguageOption> GetLanguageOptions()
    {
        var options = new List<LanguageOption>();
        foreach (var culture in TranslationManager.Instance.GetAvailableCultures())
        {
            options.Add(new LanguageOption(culture, LanguageOption.GetLanguageDisplayName(culture)));
        }
        return options;
    }

    /// <param name="isDark">Null returns every theme; true/false filters to just that theme's own
    /// declared IsDark flavor -- used by the "follow system" light/dark pickers so a dark-flavored
    /// theme can't end up selected as the "light" half of the pair.</param>
    public static IReadOnlyList<ThemeOption> GetThemeOptions(bool? isDark = null)
    {
        var options = new List<ThemeOption>();
        foreach (var t in ThemeManager.Instance.GetAvailableThemes())
        {
            if (isDark.HasValue && t.IsDark != isDark.Value) continue;
            options.Add(new ThemeOption(t.Id, t.DisplayName));
        }
        return options;
    }

    public static LogLevel ParseLogLevel(string? value) => value switch
    {
        "Error" => LogLevel.Error,
        "Warn" => LogLevel.Warn,
        "Debug" => LogLevel.Debug,
        _ => LogLevel.Info
    };

    public static string NormalizeLogLevel(string? value) => value switch
    {
        "Error" => "Error",
        "Warn" => "Warn",
        "Debug" => "Debug",
        _ => "Info"
    };
}
