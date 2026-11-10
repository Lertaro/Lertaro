using Lertaro.Core;

namespace Lertaro.App.Helpers;

// The app-wide plugin query token prefix, read from settings with the shipped default applied.
//
// One place, because five callers need the identical answer: the two dispatch controllers, the pipe service,
// the quick panel's source loader, and the delegate the SDK hands to plugins (PluginSdkBridge). Four copies
// of this one-liner already existed, all read on the search hot path; adding the fifth is where "they must
// agree" stops being something to hope for. See SearchSyntaxReserved for which characters it may be, and
// QueryTokenScanner for what it decides.
public static class GlobalTokenPrefix
{
    // The shipped default, and the fallback for a persisted empty value -- GeneralSettingsApplier resolves a
    // blank field to this too, so the two cannot disagree about what "unset" means.
    public const char Default = '\\';

    /// <summary>The configured prefix character, or <see cref="Default"/> when nothing is configured.</summary>
    public static char Current => For(UserSettings.Load());

    /// <summary>
    /// The prefix a search actually uses: the saved character, or <see cref="Default"/> when it cannot work.
    /// </summary>
    /// <remarks>
    /// An unusable saved value -- a character a file name may contain, or one the syntax owns -- is ignored
    /// rather than honored. Honoring it turns every word opening with it into a token nobody claims, so the
    /// search answers with nothing while the box shows a query that looks fine; the settings page reports
    /// that value and refuses to save it, and this is the read-side half of the same refusal.
    /// </remarks>
    public static char For(UserSettings settings)
    {
        var prefix = settings.GlobalTokenPrefix;
        return string.IsNullOrEmpty(prefix) || !SearchSyntaxReserved.IsUsableTokenPrefix(prefix[0])
            ? Default
            : prefix[0];
    }
}
