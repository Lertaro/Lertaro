using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lertaro.App.Helpers;

namespace Lertaro.App.Tests.Helpers;

// Writing a bitmap costs a WPF image type, and MSTest's own threads are MTA, so every test here takes
// the STA attribute.
//
// The two WriteTempPng tests share one real folder, and names come from the clock: run in parallel they
// both pick the same name for the same second, and the loser hits the file the other still holds open.
[TestClass]
[DoNotParallelize]
public sealed class ClipboardImageHelperTests
{
    private readonly List<string> _created = new();

    [TestCleanup]
    public void DeleteWhatTheTestsWrote()
    {
        foreach (var path in _created)
        {
            try { File.Delete(path); } catch (IOException) { }
        }

        _created.Clear();
    }

    [StaTestMethod]
    public void WriteTempPng_WritesADecodablePngNamedForTheClipboard()
    {
        var path = ClipboardImageHelper.WriteTempPng(SolidBitmap(4, 2));
        _created.Add(path);

        Assert.IsTrue(Regex.IsMatch(Path.GetFileName(path), @"^clipboard-\d{8}-\d{6}(-\d+)?\.png$"));
        Assert.AreEqual(Path.Combine(Path.GetTempPath(), "Lertaro", "Clipboard"), Path.GetDirectoryName(path));

        var header = new byte[8];
        using (var stream = File.OpenRead(path)) Assert.AreEqual(8, stream.Read(header, 0, 8));
        CollectionAssert.AreEqual(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }, header);

        using (var stream = File.OpenRead(path))
        {
            var frame = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            Assert.AreEqual(4, frame.PixelWidth);
            Assert.AreEqual(2, frame.PixelHeight);
        }
    }

    [StaTestMethod]
    public void WriteTempPng_SecondCopyKeepsTheFirstOnesFile()
    {
        var first = ClipboardImageHelper.WriteTempPng(SolidBitmap(4, 2));
        _created.Add(first);
        var second = ClipboardImageHelper.WriteTempPng(SolidBitmap(8, 1));
        _created.Add(second);

        Assert.AreNotEqual(first, second);
        CollectionAssert.AreEqual(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, File.ReadAllBytes(first)[..4]);
    }

    [StaTestMethod]
    public void NextTempPath_NameAlreadyTaken_PicksAnotherFileInTheSameFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Lertaro-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var taken = ClipboardImageHelper.NextTempPath(folder);
            File.WriteAllText(taken, "an earlier copy owns this name");

            var next = ClipboardImageHelper.NextTempPath(folder);

            Assert.AreNotEqual(taken, next);
            Assert.AreEqual(folder, Path.GetDirectoryName(next));
            Assert.AreEqual("an earlier copy owns this name", File.ReadAllText(taken));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static BitmapSource SolidBitmap(int width, int height) =>
        new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
}
