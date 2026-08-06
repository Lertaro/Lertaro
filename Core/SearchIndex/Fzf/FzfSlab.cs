namespace Lertaro.Core.SearchIndex.Fzf;

internal sealed class FzfSlab
{
    private char[] _chars = Array.Empty<char>();
    private short[] _bonus = Array.Empty<short>();
    private int[] _first = Array.Empty<int>();
    private short[] _scores = Array.Empty<short>();
    private short[] _consecutive = Array.Empty<short>();

    public char[] Chars(int length)
    {
        if (_chars.Length < length)
            _chars = new char[Grow(length)];
        return _chars;
    }

    public short[] Bonus(int length)
    {
        if (_bonus.Length < length)
            _bonus = new short[Grow(length)];
        return _bonus;
    }

    public int[] First(int length)
    {
        if (_first.Length < length)
            _first = new int[Grow(length)];
        return _first;
    }

    public short[] Scores(int length)
    {
        if (_scores.Length < length)
            _scores = new short[Grow(length)];
        return _scores;
    }

    public short[] Consecutive(int length)
    {
        if (_consecutive.Length < length)
            _consecutive = new short[Grow(length)];
        return _consecutive;
    }

    private static int Grow(int length)
    {
        var size = 256;
        while (size < length)
            size *= 2;
        return size;
    }
}
