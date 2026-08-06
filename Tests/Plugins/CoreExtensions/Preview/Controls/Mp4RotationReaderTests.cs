using System.Buffers.Binary;
using System.Text;
using Lertaro.Plugins.CoreExtensions.Preview.Controls;

namespace Lertaro.Plugins.CoreExtensions.Tests.Preview.Controls;

[TestClass]
public sealed class Mp4RotationReaderTests
{
    private const int FixedOne = 0x00010000; // 1.0 in the tkhd matrix's 16.16 fixed-point format

    private static byte[] MakeBox(string type, byte[] payload)
    {
        var result = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(result, (uint)result.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(result.AsSpan(4));
        payload.CopyTo(result.AsSpan(8));
        return result;
    }

    private static byte[] MakeContainer(string type, params byte[][] children) =>
        MakeBox(type, children.SelectMany(c => c).ToArray());

    private static byte[] MakeTkhd(int matrixA, int matrixB)
    {
        var payload = new byte[40 + 36 + 8]; // version/flags/times/reserved/layer/... + 3x3 matrix + width/height
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(40, 4), matrixA);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(44, 4), matrixB);
        return MakeBox("tkhd", payload);
    }

    private static byte[] MakeVideoTrack(int matrixA, int matrixB)
    {
        var minf = MakeContainer("minf", MakeBox("vmhd", new byte[12]));
        var mdia = MakeContainer("mdia", minf);
        return MakeContainer("trak", MakeTkhd(matrixA, matrixB), mdia);
    }

    private static MemoryStream BuildFile(params byte[][] topLevelBoxes) =>
        new(topLevelBoxes.SelectMany(b => b).ToArray());

    [TestMethod]
    public void GetRotationDegrees_IdentityMatrix_ReturnsZero()
    {
        using var stream = BuildFile(MakeContainer("moov", MakeVideoTrack(FixedOne, 0)));

        Assert.AreEqual(0, Mp4RotationReader.GetRotationDegrees(stream));
    }

    [TestMethod]
    public void GetRotationDegrees_90DegreeMatrix_Returns90()
    {
        using var stream = BuildFile(MakeContainer("moov", MakeVideoTrack(0, FixedOne)));

        Assert.AreEqual(90, Mp4RotationReader.GetRotationDegrees(stream));
    }

    [TestMethod]
    public void GetRotationDegrees_180DegreeMatrix_Returns180()
    {
        using var stream = BuildFile(MakeContainer("moov", MakeVideoTrack(-FixedOne, 0)));

        Assert.AreEqual(180, Mp4RotationReader.GetRotationDegrees(stream));
    }

    [TestMethod]
    public void GetRotationDegrees_270DegreeMatrix_Returns270()
    {
        using var stream = BuildFile(MakeContainer("moov", MakeVideoTrack(0, -FixedOne)));

        Assert.AreEqual(270, Mp4RotationReader.GetRotationDegrees(stream));
    }

    [TestMethod]
    public void GetRotationDegrees_TrackWithoutVideoMediaHeader_ReturnsZero()
    {
        // An audio-only trak (no vmhd under mdia/minf) must not be mistaken for the video track, even
        // though its tkhd happens to carry a non-identity matrix.
        var minf = MakeContainer("minf", MakeBox("smhd", new byte[8])); // sound media header, not video
        var mdia = MakeContainer("mdia", minf);
        var audioTrak = MakeContainer("trak", MakeTkhd(0, FixedOne), mdia);
        using var stream = BuildFile(MakeContainer("moov", audioTrak));

        Assert.AreEqual(0, Mp4RotationReader.GetRotationDegrees(stream));
    }

    [TestMethod]
    public void GetRotationDegrees_NoMoovBox_ReturnsZero()
    {
        using var stream = BuildFile(MakeBox("ftyp", Encoding.ASCII.GetBytes("isom")));

        Assert.AreEqual(0, Mp4RotationReader.GetRotationDegrees(stream));
    }

    [TestMethod]
    public void GetRotationDegrees_EmptyStream_ReturnsZero()
    {
        using var stream = new MemoryStream();

        Assert.AreEqual(0, Mp4RotationReader.GetRotationDegrees(stream));
    }

    [TestMethod]
    public void GetRotationDegrees_NotAContainerAtAll_ReturnsZero()
    {
        // A renamed .txt (or any non-MP4 file with an .mp4/.mov extension): plausible-looking bytes that
        // don't form valid boxes must fail closed instead of throwing.
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("this is not a video file, just text"));

        Assert.AreEqual(0, Mp4RotationReader.GetRotationDegrees(stream));
    }

    [TestMethod]
    public void GetRotationDegrees_TruncatedMidBox_ReturnsZero()
    {
        var moov = MakeContainer("moov", MakeVideoTrack(0, FixedOne));
        using var stream = BuildFile(moov[..(moov.Length - 20)]); // cut off before the matrix is fully written

        Assert.AreEqual(0, Mp4RotationReader.GetRotationDegrees(stream));
    }

    [TestMethod]
    public void GetRotationDegrees_BoxSizeExceedsStreamLength_ReturnsZeroInsteadOfReadingPastEnd()
    {
        // A box that lies about its own size (declares more than actually exists) must be rejected rather
        // than trusted -- guards against a maliciously crafted or corrupted file.
        var bogus = new byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(bogus, 0xFFFFFF); // declares far more bytes than this stream has
        Encoding.ASCII.GetBytes("moov").CopyTo(bogus.AsSpan(4));
        using var stream = new MemoryStream(bogus);

        Assert.AreEqual(0, Mp4RotationReader.GetRotationDegrees(stream));
    }

    [TestMethod]
    public void GetRotationDegrees_NonexistentPath_ReturnsZero() =>
        Assert.AreEqual(0, Mp4RotationReader.GetRotationDegrees(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));
}
