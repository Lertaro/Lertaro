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
    /// The plugin now reads the prefix from the search syntax itself (the SDK's SearchSyntaxService), so the
    /// stored copy can only ever be stale: it names a character its own tokens no longer answer to, and any
    /// sidebar filter rule written with it stops expanding. Cleared rather than migrated, because there is no
    /// new key to migrate it to.
    ///
    /// This is also why the notice built on it needs no "already shown" flag of its own: deleting the key is
    /// what makes the condition unrepeatable, so a second launch cannot nag.
    /// </remarks>
    public static string? ClearLegacyFilterPrefix(UserSettings settings)
    {
        var stored = settings.GetPluginSetting<string?>(LegacyFilterPrefixPluginId, LegacyFilterPrefixKey, null);
        if (string.IsNullOrEmpty(stored))
            return null;

        settings.SetPluginSetting(LegacyFilterPrefixPluginId, LegacyFilterPrefixKey, null);
        return stored == settings.GlobalTokenPrefix ? null : stored;
    }
}
