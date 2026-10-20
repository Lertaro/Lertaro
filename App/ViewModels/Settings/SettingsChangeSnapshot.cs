using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings;

internal static class SettingsChangeSnapshot
{
    public static ExclusionSnapshot CaptureExclusions(UserSettings settings) => new(
        IndexRuleMigration.Ensure(settings)
            .Select(rule => new IndexRuleSnapshot(rule.Pattern, rule.Effect))
            .ToList());

    /// <summary>
    /// Only the excluding subset matters to the index: low/normal/high rules never change what a network
    /// walk skips, so moving between them must not trigger a rescan.
    /// </summary>
    public static bool ExclusionsChanged(ExclusionSnapshot oldRules, ExclusionSnapshot newRules)
        => !ExcludePatterns(oldRules).SequenceEqual(ExcludePatterns(newRules), StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> ExcludePatterns(ExclusionSnapshot snapshot) => snapshot.Rules
        .Where(rule => rule.Effect == IndexRuleEffect.Exclude)
        .Select(rule => IndexRuleSyntax.Clean(rule.Pattern))
        .Where(pattern => pattern.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(pattern => pattern, StringComparer.OrdinalIgnoreCase);

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

internal sealed record ExclusionSnapshot(IReadOnlyList<IndexRuleSnapshot> Rules);

internal sealed record IndexRuleSnapshot(string Pattern, IndexRuleEffect Effect);
