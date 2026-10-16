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
        // A placeholder/hint string isn't a rooted path; canonical note at BandizipPathHelpersTests.
        var result = WinRARExtractDialogAdapter.NormalizeIfWellFormed("choose a folder...");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void NormalizeIfWellFormed_DriveRoot_KeepsTrailingBackslash()
    {
        // "D:" alone is a different path than "D:\" (current dir on that drive vs. its root) -- full
        // confirmed-live reason at Plugins/Bandizip/BandizipPathHelpers.cs, which documents both copies.
        var result = WinRARExtractDialogAdapter.NormalizeIfWellFormed(@"D:\");

        Assert.AreEqual(@"D:\", result);
    }

    [TestMethod]
    public void NormalizeIfWellFormed_NetworkDrive_ReturnsItEvenThoughUnreachableFromHere()
    {
        // Dropping the Directory.Exists check is the whole point here: a mapped network drive the
        // interactive user can see is invisible to the elevated Hook process this runs in -- full reason
        // (including the confirmed-live SearchScope freeze) at Plugins/Bandizip/BandizipPathHelpers.cs.
        var result = WinRARExtractDialogAdapter.NormalizeIfWellFormed(@"Z:\share\projects\build");

        Assert.AreEqual(@"Z:\share\projects\build", result);
    }
}
