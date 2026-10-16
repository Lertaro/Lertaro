using System.Text;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.Tests.IndexV2.Alias;

// Minimal IAliasGridProvider for the grid tests: readings and boundaries are handed in verbatim, so the
// codec and the matcher are exercised against a known alias space instead of the pinyin table's.
internal sealed class FakeGridProvider : IAliasGridProvider
{
    // The pinyin provider's delimiter value. Core never hardcodes it -- the snapshot carries a provider
    // id and AliasProviderRegistry resolves the separator from there -- so the tests just reuse the same
    // value the pinyin plugin uses.
    public const char Separator = (char)2;

    private readonly string[][] _sets;
    private readonly bool[] _boundaries;

    public FakeGridProvider(string[][] sets, bool[]? boundaries = null, char separator = Separator)
    {
        _sets = sets;
        _boundaries = boundaries ?? new bool[sets.Length];
        SyllableSeparator = separator;
    }

    public bool CanHandle(string text) => true;

    public IReadOnlyList<(char Start, char End)> InputRanges => Array.Empty<(char Start, char End)>();

    public IReadOnlyList<(char Start, char End)> OutputRanges => new[] { ('a', 'z') };

    public char SyllableSeparator { get; }

    public IEnumerable<string> GetAliases(string text) => new[] { string.Concat(_sets.Select(s => s[0][0])) };

    public string[][] GetReadingSets(string text) => _sets;

    public bool NeedsSeparatorBefore(string text, int index)
        => index > 0 && index < _boundaries.Length && _boundaries[index];

    // The spelled-out form of the same readings, mirroring a provider's byte path: the initials alias
    // (one character per position) followed by the full alias when it differs.
    public void GetReadingAliasesUtf8(string text, AliasByteSink dest)
    {
        var initials = new StringBuilder();
        var full = new StringBuilder();
        for (var i = 0; i < _sets.Length; i++)
        {
            initials.Append(_sets[i][0][0]);
            if (NeedsSeparatorBefore(text, i))
                full.Append(Separator);
            full.Append(_sets[i][0]);
        }

        dest.AddString(initials.ToString());
        if (full.ToString() != initials.ToString())
            dest.AddString(full.ToString());
    }

    public void GetStandaloneAliasesUtf8(string text, AliasByteSink dest)
    {
    }
}
