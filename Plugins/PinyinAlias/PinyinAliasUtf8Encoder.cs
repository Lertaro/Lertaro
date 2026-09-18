using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.PinyinAlias;

// Extracted out of PinyinAliasProvider.cs (composition, not a partial class) purely to keep that
// file under the repo's per-file line limit. It emits every alias a name has: the pinyin readings
// (delegated to PinyinAliasUtf8PinyinEncoder, which is the byte-native mirror of the string path's
// combination generator) plus the Simplified spelling of a Traditional name, which is what makes a
// Simplified query reach it. The host's bulk indexing path calls this instead of the string API, so
// anything GetAliases yields has to be emitted here too or it never reaches the index.
internal static class PinyinAliasUtf8Encoder
{
    public static void Encode(string text, AliasByteSink dest, bool convertEnabled, Func<string, string> simplify)
    {
        if (string.IsNullOrEmpty(text))
            return;

        PinyinAliasUtf8PinyinEncoder.Encode(text, dest);

        // After the pinyin segments, so the string path's order (pinyin first, then the Simplified
        // spelling) is reproduced -- which is what keeps the two paths' output comparable.
        AppendSimplified(text, dest, convertEnabled, simplify);
    }

    // The Simplified spelling of a Traditional name, as one plain segment. Shared by both provider
    // paths, hence the injected converter rather than a direct HanConversion call.
    private static void AppendSimplified(string text, AliasByteSink dest, bool convertEnabled, Func<string, string> simplify)
    {
        if (!convertEnabled)
            return;

        var simplified = simplify(text);
        if (string.IsNullOrEmpty(simplified) || string.Equals(simplified, text, StringComparison.Ordinal))
            return;

        dest.AddString(simplified);
    }
}
