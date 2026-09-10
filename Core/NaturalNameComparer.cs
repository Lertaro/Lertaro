using System.Runtime.InteropServices;

namespace Lertaro.Core;

// Explorer's own file-name ordering (natural/version sort): digits compare by value, so "9.png" precedes
// "10.png", and punctuation is handled the way the shell does rather than by raw character code.
//
// Used only where the user is looking at ONE folder as a folder -- the inline window's own directory --
// because that is where they are comparing against what Explorer itself shows. Results from a subtree
// search stay on match-quality ordering, which is what makes a broad search useful.
//
// Stateless and side-effect free, so it is safe to call per comparison from a sort.
public sealed class NaturalNameComparer : IComparer<string>
{
    public static readonly NaturalNameComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x is null)
            return -1;
        if (y is null)
            return 1;

        var result = TryCompareLogical(x, y, out var compare);
        return result ? compare : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
    }

    // shlwapi is present on every supported Windows, but a failure here must degrade to a plain
    // case-insensitive compare rather than take a search down -- ordering is a nicety, not correctness.
    private static bool TryCompareLogical(string x, string y, out int compare)
    {
        try
        {
            compare = StrCmpLogicalW(x, y);
            return true;
        }
        catch (DllNotFoundException)
        {
            compare = 0;
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            compare = 0;
            return false;
        }
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);
}
