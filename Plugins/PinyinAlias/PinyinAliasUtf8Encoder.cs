using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.PinyinAlias;

// Byte-native GetAliasesUtf8 for the bulk indexing path. Since the readings are decided live
// (IAliasLiveMatcherProvider; see PinyinTermMatcher), the only alias this provider emits is the
// Simplified spelling of a Traditional name -- which is what makes a Simplified query reach a
// Traditional candidate through the string-alias tier. The spelled-out pinyin half this file used to
// concatenate ahead of it is gone with the index-side reading storage it fed.
internal static class PinyinAliasUtf8Encoder
{
    public static void Encode(string text, AliasByteSink dest, bool convertEnabled, Func<string, string> simplify)
    {
        if (string.IsNullOrEmpty(text))
            return;

        AppendSimplified(text, dest, convertEnabled, simplify);
    }

    // The Simplified spelling of a Traditional name, as one plain segment. Shared by the string and
    // byte paths (and by the live provider's standalone emission), hence the injected converter rather
    // than a direct HanConversion call.
    internal static void AppendSimplified(string text, AliasByteSink dest, bool convertEnabled, Func<string, string> simplify)
    {
        if (!convertEnabled)
            return;

        var simplified = simplify(text);
        if (string.IsNullOrEmpty(simplified) || string.Equals(simplified, text, StringComparison.Ordinal))
            return;

        dest.AddString(simplified);
    }
}
