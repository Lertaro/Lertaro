using Lertaro.Plugins.CoreExtensions.Preview.Providers;

namespace Lertaro.Plugins.CoreExtensions.Tests.Preview.Providers;

[TestClass]
public sealed class ImagePreviewProviderTests
{
    private static readonly ImagePreviewProvider Provider = new();

    [TestMethod]
    public void CanPreview_PngFile_ReturnsTrue() => Assert.IsTrue(Provider.CanPreview(@"C:\photo.png", isDir: false));

    [TestMethod]
    public void CanPreview_ExtensionMatchIsCaseInsensitive() => Assert.IsTrue(Provider.CanPreview(@"C:\Photo.PNG", isDir: false));

    [TestMethod]
    public void CanPreview_OtherExtension_ReturnsFalse() => Assert.IsFalse(Provider.CanPreview(@"C:\readme.txt", isDir: false));

    [TestMethod]
    public void CanPreview_Directory_ReturnsFalseEvenWithImageExtension() => Assert.IsFalse(Provider.CanPreview(@"C:\photo.png", isDir: true));
}

[TestClass]
public sealed class TextPreviewProviderTests
{
    private static readonly TextPreviewProvider Provider = new();

    private sealed class TempFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lertaro-tests-{Guid.NewGuid():N}");

        // Default fill is ASCII 'a', not the CLR's zero-initialized default -- an all-zero buffer is
        // itself binary content (a run of null bytes), which would make every "should be text-previewable"
        // fixture accidentally fail LooksBinary's own null-byte check.
        public TempFile(int sizeBytes) : this(new string('a', sizeBytes)) { }

        public TempFile(string content) => File.WriteAllText(Path, content);

        public TempFile(byte[] bytes) => File.WriteAllBytes(Path, bytes);

        public void Dispose()
        {
            try { File.Delete(Path); } catch { }
        }
    }

    [TestMethod]
    public void CanPreview_Directory_ReturnsFalse() => Assert.IsFalse(Provider.CanPreview(@"C:\somefile.unknownext", isDir: true));

    [TestMethod]
    public void CanPreview_KnownTextExtension_ReturnsTrueEvenIfFileMissing() =>
        // Extension match short-circuits before any FileInfo access, so a nonexistent path is fine.
        Assert.IsTrue(Provider.CanPreview(@"C:\definitely-missing.cs", isDir: false));

    [TestMethod]
    public void CanPreview_UnknownExtension_SmallExistingTextFile_ReturnsTrue()
    {
        using var file = new TempFile(1024);

        Assert.IsTrue(Provider.CanPreview(file.Path, isDir: false));
    }

    [TestMethod]
    public void CanPreview_UnknownExtension_LargeExistingFile_ReturnsFalse()
    {
        using var file = new TempFile(200_000);

        Assert.IsFalse(Provider.CanPreview(file.Path, isDir: false));
    }

    [TestMethod]
    public void CanPreview_UnknownExtension_MissingFile_ReturnsFalse() =>
        Assert.IsFalse(Provider.CanPreview(@"Z:\definitely-not-real-lertaro-file.unknownext", isDir: false));

    [TestMethod]
    public void CanPreview_UnknownExtension_SmallBinaryFile_ReturnsFalse()
    {
        // A null byte anywhere in the first 4KB flags it as binary (e.g. a small .zip's compressed
        // stream) -- must not be claimed here just because it's small and has an unrecognized extension,
        // otherwise it pre-empts every lower-priority provider that could actually preview it.
        using var file = new TempFile(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00 });

        Assert.IsFalse(Provider.CanPreview(file.Path, isDir: false));
    }

    [TestMethod]
    public void CanPreview_KnownTextExtension_SmallBinaryContent_StillReturnsTrue()
    {
        // Extension match short-circuits before the binary sniff runs -- unchanged from before this fix,
        // a .cs/.json/etc. file is trusted by extension alone.
        var path = Path.Combine(Path.GetTempPath(), $"lertaro-tests-{Guid.NewGuid():N}.cs");
        File.WriteAllBytes(path, new byte[] { 0x00, 0x00, 0x00 });
        try
        {
            Assert.IsTrue(Provider.CanPreview(path, isDir: false));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}

[TestClass]
public sealed class DefaultMetadataPreviewProviderTests
{
    private static readonly DefaultMetadataPreviewProvider Provider = new();

    [TestMethod]
    public void CanPreview_AnyFile_ReturnsTrue() => Assert.IsTrue(Provider.CanPreview(@"C:\anything.whatever", isDir: false));

    [TestMethod]
    public void CanPreview_Directory_ReturnsTrue() => Assert.IsTrue(Provider.CanPreview(@"C:\somedir", isDir: true));
}

[TestClass]
public sealed class FolderPreviewProviderTests
{
    private static readonly FolderPreviewProvider Provider = new();

    [TestMethod]
    public void CanPreview_Directory_ReturnsTrue() => Assert.IsTrue(Provider.CanPreview(@"C:\somedir", isDir: true));

    [TestMethod]
    public void CanPreview_File_ReturnsFalse() => Assert.IsFalse(Provider.CanPreview(@"C:\file.txt", isDir: false));

    [TestMethod]
    [DataRow(FileAttributes.Normal, true)]
    [DataRow(FileAttributes.Directory, true)]
    [DataRow(FileAttributes.Hidden, false)]
    [DataRow(FileAttributes.System, false)]
    [DataRow(FileAttributes.Directory | FileAttributes.Hidden, false)]
    [DataRow(FileAttributes.Directory | FileAttributes.System, false)]
    [DataRow(FileAttributes.Hidden | FileAttributes.System, false)]
    public void ShouldDisplay_FiltersHiddenAndSystemItems(FileAttributes attributes, bool expected) =>
        Assert.AreEqual(expected, FolderPreviewProvider.ShouldDisplay(attributes));
}
