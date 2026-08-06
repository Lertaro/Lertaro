using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings;

internal static class SettingsChangeSnapshot
{
    public static ExclusionSnapshot CaptureExclusions(UserSettings settings) => new(
        settings.ExcludedPaths.ToList(),
        settings.IgnoredPathGlobs.ToList(),
        settings.IgnoredPathRegexes.ToList());

    public static bool ExclusionsChanged(ExclusionSnapshot oldRules, ExclusionSnapshot newRules)
        => StringListChanged(oldRules.Paths, newRules.Paths)
        || StringListChanged(oldRules.Globs, newRules.Globs)
        || StringListChanged(oldRules.Regexes, newRules.Regexes);

    public static bool StringListChanged(IReadOnlyList<string> oldValues, IReadOnlyList<string> newValues) => !oldValues
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .Select(v => v.Trim())
        .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
        .SequenceEqual(
            newValues
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
}

internal sealed record ExclusionSnapshot(
    IReadOnlyList<string> Paths,
    IReadOnlyList<string> Globs,
    IReadOnlyList<string> Regexes);
