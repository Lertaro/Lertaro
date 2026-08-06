namespace Lertaro.Plugins.WinRAR.Tests;

[TestClass]
public sealed class WinRARExtractDialogAdapterTests
{
    [TestMethod]
    public void NormalizeIfWellFormed_RootedPath_ReturnsItTrimmed()
    {
        var result = WinRARExtractDialogAdapter.NormalizeIfWellFormed(@"C:\Users\testuser\Desktop\");

        Assert.AreEqual(@"C:\Users\testuser\Desktop", result);
    }

    [TestMethod]
    public void NormalizeIfWellFormed_NotYetCreatedFolder_StillReturnsIt()
    {
        // WinRAR's own default extraction folder is one it plans to create -- it commonly doesn't exist
        // yet. Unlike the old strict "must already exist" contract, this no longer rejects it: existence
        // can't be verified reliably from the elevated Hook process this runs in anyway (see
        // NormalizeIfWellFormed's own comment), so a well-formed path is trusted regardless.
        var result = WinRARExtractDialogAdapter.NormalizeIfWellFormed(@"C:\Users\testuser\Desktop\New ZIP Archive");

        Assert.AreEqual(@"C:\Users\testuser\Desktop\New ZIP Archive", result);
    }

    [TestMethod]
    public void NormalizeIfWellFormed_EmptyText_ReturnsNull()
    {
        var result = WinRARExtractDialogAdapter.NormalizeIfWellFormed("");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void NormalizeIfWellFormed_WhitespaceText_ReturnsNull()
    {
        var result = WinRARExtractDialogAdapter.NormalizeIfWellFormed("   ");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void NormalizeIfWellFormed_NotRootedText_ReturnsNull()
    {
        // A placeholder/hint string (or any non-path text) isn't a rooted path -- must still be rejected
        // even without an existence check.
        var result = WinRARExtractDialogAdapter.NormalizeIfWellFormed("choose a folder...");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void NormalizeIfWellFormed_DriveRoot_KeepsTrailingBackslash()
    {
        // "D:" alone is a different path than "D:\" (current directory on that drive vs. its root) --
        // trimming the drive root's trailing separator must not produce the former. Confirmed live via
        // app.log showing a bare "D:" SearchScope breaking Path.GetRelativePath downstream.
        var result = WinRARExtractDialogAdapter.NormalizeIfWellFormed(@"D:\");

        Assert.AreEqual(@"D:\", result);
    }

    [TestMethod]
    public void NormalizeIfWellFormed_NetworkDrive_ReturnsItEvenThoughUnreachableFromHere()
    {
        // The whole point of dropping the Directory.Exists check: a mapped network drive the interactive
        // user can see is invisible to the elevated Hook process this runs in, so verifying it here would
        // wrongly reject a perfectly real path. Confirmed live: this used to silently freeze SearchScope at
        // its last value once the dialog's target moved onto a network drive.
        var result = WinRARExtractDialogAdapter.NormalizeIfWellFormed(@"Z:\share\projects\build");

        Assert.AreEqual(@"Z:\share\projects\build", result);
    }
}
