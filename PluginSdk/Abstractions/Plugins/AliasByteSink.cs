using System.Text;

namespace Lertaro.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Receives alias output as UTF-8 byte segments for <see cref="IAliasProvider.GetAliasesUtf8"/> --
/// one growable buffer plus a segment list, owned and reused by the host, so a provider can emit
/// aliases without materializing any intermediate string. The snapshot stores aliases as UTF-8
/// bytes, so on the bulk indexing path this skips both the alias string allocation and the
/// re-encode the string API otherwise requires.
/// </summary>
public sealed class AliasByteSink
{
    private byte[] _buffer = new byte[2048];
    private int _length;
    private readonly List<(int Start, int Len)> _segments = new(4);

    /// <summary>Number of completed alias segments.</summary>
    public int SegmentCount => _segments.Count;

    /// <summary>Returns completed segment <paramref name="i"/> as a UTF-8 byte span.</summary>
    public ReadOnlySpan<byte> Segment(int i)
    {
        var (start, len) = _segments[i];
        return _buffer.AsSpan(start, len);
    }

    /// <summary>Clears all segments and buffered bytes for reuse.</summary>
    public void Reset()
    {
        _length = 0;
        _segments.Clear();
    }

    /// <summary>
    /// Adds a complete alias as one segment, encoding it to UTF-8. This is what the default
    /// (string-API fallback) implementation of <see cref="IAliasProvider.GetAliasesUtf8"/> uses.
    /// </summary>
    public void AddString(string alias)
    {
        var start = _length;
        Ensure(alias.Length * 3);
        _length += Encoding.UTF8.GetBytes(alias.AsSpan(), _buffer.AsSpan(_length));
        _segments.Add((start, _length - start));
    }

    /// <summary>Starts an open segment at the current position; finish it with <see cref="EndSegment"/>.</summary>
    public int BeginSegment() => _length;

    /// <summary>Completes the segment opened at <paramref name="segmentStart"/>. A zero-length segment is discarded.</summary>
    public void EndSegment(int segmentStart)
    {
        if (_length > segmentStart)
            _segments.Add((segmentStart, _length - segmentStart));
    }

    /// <summary>Discards any bytes written after <paramref name="segmentStart"/> without adding a segment.</summary>
    public void AbandonSegment(int segmentStart) => _length = segmentStart;

    /// <summary>The bytes written since <paramref name="segmentStart"/> that are not yet a completed
    /// segment -- lets a provider compare an in-progress alias against a completed one before deciding
    /// to keep (<see cref="EndSegment"/>) or drop (<see cref="AbandonSegment"/>) it.</summary>
    public ReadOnlySpan<byte> Pending(int segmentStart) => _buffer.AsSpan(segmentStart, _length - segmentStart);

    /// <summary>Appends one raw byte to the open segment.</summary>
    public void Append(byte b)
    {
        Ensure(1);
        _buffer[_length++] = b;
    }

    /// <summary>Appends raw UTF-8 bytes to the open segment.</summary>
    public void Append(ReadOnlySpan<byte> bytes)
    {
        Ensure(bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(_length));
        _length += bytes.Length;
    }

    private void Ensure(int extra)
    {
        if (_length + extra > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + extra));
    }
}
