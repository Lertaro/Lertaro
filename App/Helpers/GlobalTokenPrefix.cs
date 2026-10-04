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
    public static char Current
    {
        get
        {
            var prefix = UserSettings.Load().GlobalTokenPrefix;
            return string.IsNullOrEmpty(prefix) ? Default : prefix[0];
        }
    }
}
