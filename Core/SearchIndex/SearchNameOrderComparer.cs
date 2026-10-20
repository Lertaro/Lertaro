namespace Lertaro.Core.SearchIndex;

// Final display tie-breaker shared by both windows: digits first, then letters (uppercase before
// lowercase), digits in numeric order ("a2" before "a10"), and a shorter prefix first. Digit runs are
// compared by significant length then ordinal, so a 20-digit timestamp can never overflow an integer
// parse (the previous implementation called long.Parse and threw on such names).
public sealed class SearchNameOrderComparer : IComparer<string>
{
    public static readonly SearchNameOrderComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            var cx = x[i];
            var cy = y[j];
            if (char.IsDigit(cx) && char.IsDigit(cy))
            {
                var si = i;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                var sj = j;
                while (j < y.Length && char.IsDigit(y[j])) j++;

                var nx = SkipLeadingZeros(x.AsSpan(si, i - si));
                var ny = SkipLeadingZeros(y.AsSpan(sj, j - sj));
                if (nx.Length != ny.Length)
                    return nx.Length.CompareTo(ny.Length);

                var digits = nx.SequenceCompareTo(ny);
                if (digits != 0)
                    return digits;
                continue;
            }

            var kx = ClassOf(cx);
            var ky = ClassOf(cy);
            if (kx != ky) return kx.CompareTo(ky);
            if (cx != cy) return cx.CompareTo(cy);
            i++;
            j++;
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }

    private static ReadOnlySpan<char> SkipLeadingZeros(ReadOnlySpan<char> digits)
    {
        var start = 0;
        while (start < digits.Length && digits[start] == '0')
            start++;
        return digits[start..];
    }

    private static int ClassOf(char c) => char.IsDigit(c) ? 0 : char.IsLetter(c) ? 1 : 2;
}
