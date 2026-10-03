using System.Text;
using Lertaro.Plugins.ContentSearch.Extraction;

namespace Lertaro.Plugins.ContentSearch.Tests.Extraction;

[TestClass]
public sealed class ExtractionLimitsTests
{
    [TestMethod]
    public void ReadBounded_DeclaredLengthOverCeiling_ThrowsWithoutReadingTheStream()
    {
        // The declared length comes from the archive's central directory, so a bomb entry is
        // rejected before a single byte is decompressed. The stream must stay untouched.
        var source = new CountingStream(new byte[16]);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            ExtractionLimits.ReadBounded(source, ExtractionLimits.MaxEntryUncompressedBytes + 1, "word/document.xml"));

        Assert.AreEqual(0, source.BytesRead, "a declared over-limit entry must not be read at all");
    }

    [TestMethod]
    public void ReadBounded_DeclaredLengthSmallButDataOverCeiling_Throws()
    {
        // A lying declared length (the running total is the real bound) still cannot make the
        // reader hold more than the ceiling.
        using var source = new MemoryStream(new byte[ExtractionLimits.MaxEntryUncompressedBytes + 1]);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            ExtractionLimits.ReadBounded(source, declaredLength: 1, "lying-entry"));
    }

    [TestMethod]
    public void ReadBounded_SmallEntry_ReturnsAllBytes()
    {
        using var source = new MemoryStream("word text"u8.ToArray());

        using var result = ExtractionLimits.ReadBounded(source, declaredLength: 9, "word/document.xml");

        Assert.AreEqual("word text", Encoding.UTF8.GetString(result.ToArray()));
    }

    [TestMethod]
    public void ThrowIfOverTextLimit_AtCeilingIsAcceptedAndOneCharPastThrows()
    {
        var builder = new StringBuilder(ExtractionLimits.MaxExtractedTextChars + 1)
        {
            Length = ExtractionLimits.MaxExtractedTextChars
        };

        ExtractionLimits.ThrowIfOverTextLimit(builder);

        builder.Append('x');
        Assert.ThrowsExactly<InvalidDataException>(() => ExtractionLimits.ThrowIfOverTextLimit(builder));
    }

    /// <summary>A stream that counts the bytes actually pulled from it.</summary>
    private sealed class CountingStream : MemoryStream
    {
        public CountingStream(byte[] data) : base(data) { }

        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }
    }
}
