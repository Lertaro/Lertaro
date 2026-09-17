namespace Lertaro.Core;

/// <summary>
/// Finds settings values that a previous release wrote but the current search syntax can no longer honor,
/// or no longer consults at all, so the app can tell the user about them instead of letting the feature fail
/// quietly.
/// </summary>
/// <remarks>
/// The rewrite made '\' the plugin query token prefix. The last shipped release (v5.6.7) defaulted that
/// field to ':' -- the character that used to open a token (":audio") -- and ':' is now the exclusion
/// operator, so it is consumed by the syntax before any plugin token provider ever sees it. A settings file
/// carried over from that release therefore keeps a prefix that no longer works: every plugin token the
/// user types is read as an exclusion, the token stops filtering, and nothing on screen says why. The
/// settings page does report the value as reserved, but only once the user opens that page and looks at
/// that one field.
///
/// Detection is deliberately a pure function over the loaded settings rather than a rewrite on load: the
/// user asked to be told, not to have the value changed underneath them (a user who set ':' on purpose, or
/// who has a plugin built against the old prefix, must get the chance to decide).
/// </remarks>
public static class LegacySettingsAdvisor
{
    // The prefix the last shipped release wrote. Named rather than inlined so the reason above and the
    // value cannot drift.
    private const string ShippedLegacyTokenPrefix = ":";

    // The prefix this release ships, and the one a reset collision falls back to. The App's
    // GlobalTokenPrefix.Default and GeneralSettingsApplier's blank-to-default rule name the same character.
    private const string DefaultTokenPrefix = "\\";

    /// <summary>
    /// True when the saved plugin query token prefix is the one the previous release shipped, which the
    /// current syntax no longer accepts. False once the user has changed it -- including to another value
    /// that is also unusable, since a value the user chose themselves is the settings page's to report, not
    /// a migration's.
    /// </summary>
    public static bool HasLegacyTokenPrefix(UserSettings settings)
        => settings.GlobalTokenPrefix == ShippedLegacyTokenPrefix;

    /// <summary>
    /// True when the startup notice should be shown now: the saved prefix is still the legacy one AND the
    /// user has not already been told about it.
    /// </summary>
    /// <remarks>
    /// Once per user, not once per launch. The settings page reports the value on its own field for as long
    /// as it is set (see QueryTokenPrefixRules), so a balloon that reappears at every start would only nag
    /// about something the user has either already seen or already decided to keep. The caller marks it shown
    /// before displaying it, so a crash in between cannot turn it into a repeat either.
    /// </remarks>
    public static bool ShouldShowNotice(UserSettings settings)
        => !settings.LegacyTokenPrefixNoticeShown && HasLegacyTokenPrefix(settings);

    // The bundled CoreExtensions plugin used to keep its own copy of the token prefix under this key. Named
    // here rather than referenced from the plugin because Core cannot see plugin assemblies, and named with
    // the plugin id for the same reason a settings key is: it is persisted data, not a type.
    private const string LegacyFilterPrefixPluginId = "Lertaro.Plugins.CoreExtensions";
    private const string LegacyFilterPrefixKey = "CustomFilterPrefix";

    /// <summary>
    /// Removes that plugin's own prefix setting if a settings file still carries one, returning the value it
    /// held -- or null when there was nothing to remove, or when it agreed with the host prefix and so never
    /// meant anything different.
    /// </summary>
    /// <remarks>
    /// Named "Take" rather than "Clear" because it MUTATES the settings, and the caller has to save them for
    /// the change to stick -- the same contract as <see cref="TakePrecisionTriggerResultTypes"/>. The
    /// plugin now reads the prefix from the search syntax itself (the SDK's SearchSyntaxService), so the
    /// stored copy can only ever be stale: it names a character its own tokens no longer answer to, and any
    /// sidebar filter rule written with it stops expanding. Cleared rather than migrated, because there is no
    /// new key to migrate it to.
    ///
    /// This is also why the notice built on it needs no "already shown" flag of its own: deleting the key is
    /// what makes the condition unrepeatable, so a second launch cannot nag.
    /// </remarks>
    public static string? TakeLegacyFilterPrefix(UserSettings settings)
    {
        var stored = settings.GetPluginSetting<string?>(LegacyFilterPrefixPluginId, LegacyFilterPrefixKey, null);
        if (string.IsNullOrEmpty(stored))
            return null;

        settings.SetPluginSetting(LegacyFilterPrefixPluginId, LegacyFilterPrefixKey, null);

        // The key is redundant once it agrees with the host's value, so it is dropped either way -- but only
        // the disagreeing one is worth a balloon, and the agreeing one would otherwise vanish with no trace
        // at all. Recorded, so that decision is auditable after the fact.
        if (stored == settings.GlobalTokenPrefix)
        {
            Logger.Log($"[LegacySettings] Removed '{LegacyFilterPrefixKey}' from {LegacyFilterPrefixPluginId}: it duplicated the host's token prefix.", LogLevel.Debug);
            return null;
        }

        return stored;
    }

    // The precision-inversion trigger, as the parser reads it from a term's first character (see
    // TermTriggers, the one place that reads it). The App mirrors this in SearchSyntaxReserved, which is also
    // where the settings-time warning about it lives.
    private const char PrecisionInversion = SearchIndex.Fzf.TermTriggers.PrecisionInversion;
    private const string PrecisionInversionText = "?";

    /// <summary>
    /// True when a saved value can no longer work because '?' became the precision-inversion trigger after it
    /// was written: a '?' token prefix retires the inversion AND gets every plugin token lifted out of the
    /// query with nobody to claim it, and a '?' per-result-type trigger eats the character before the engine
    /// ever sees the query.
    /// </summary>
    public static bool HasPrecisionTriggerCollisions(UserSettings settings)
        => settings.GlobalTokenPrefix == PrecisionInversionText || TakeableResultTypes(settings).Count > 0;

    /// <summary>
    /// Puts the app-wide token prefix back to its shipped default when a settings file holds '?', returning
    /// whether it did. Mutates: the caller has to save.
    /// </summary>
    /// <remarks>
    /// The prefix is read before every word is offered to anything else, so a '?' there is the worse of the
    /// two collisions: the word is lifted out as a token, no provider claims it, and the search returns
    /// nothing at all -- which looks like the feature being broken rather than like one character being
    /// reserved. The default is restored rather than the value being reported, because there is no way to
    /// keep '?' working in both roles.
    /// </remarks>
    public static bool TakePrecisionTriggerTokenPrefix(UserSettings settings)
    {
        if (settings.GlobalTokenPrefix != PrecisionInversionText)
            return false;

        settings.GlobalTokenPrefix = DefaultTokenPrefix;
        return true;
    }

    /// <summary>
    /// Clears every per-result-type trigger that is now the precision-inversion character, returning the ones
    /// it cleared so the caller can name them. Mutates: the caller has to save.
    /// </summary>
    /// <remarks>
    /// Cleared rather than reset: "no trigger" is the working default for this feature (the type is then
    /// reached like every other one), while a trigger of '?' means the quick window reads the character as
    /// "only this result type" and searches the REST of the query with fuzzy matching -- the exact opposite
    /// of what the user typed. Idempotent, which is what makes the notice it feeds unrepeatable.
    /// </remarks>
    public static List<(string TypeId, string Trigger)> TakePrecisionTriggerResultTypes(UserSettings settings)
    {
        var taken = TakeableResultTypes(settings);
        foreach (var (typeId, _) in taken)
            settings.ResultTypeTriggers.Remove(typeId);

        return taken;
    }

    private static List<(string TypeId, string Trigger)> TakeableResultTypes(UserSettings settings)
        => settings.ResultTypeTriggers
            .Where(entry => entry.Value.StartsWith(PrecisionInversionText, StringComparison.Ordinal))
            .Select(entry => (entry.Key, entry.Value))
            .ToList();
}
