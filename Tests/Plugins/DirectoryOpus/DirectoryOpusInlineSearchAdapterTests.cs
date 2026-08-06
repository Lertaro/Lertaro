namespace Lertaro.Plugins.DirectoryOpus.Tests;

[TestClass]
public sealed class DirectoryOpusInlineSearchAdapterTests
{
    private static readonly IntPtr SomeHwnd = (IntPtr)1;

    [TestMethod]
    public void CanTrigger_FileDisplayClass_ReturnsTrue()
    {
        var adapter = new DirectoryOpusInlineSearchAdapter();

        Assert.IsTrue(adapter.CanTrigger(SomeHwnd, "dopus.filedisplay"));
    }

    [TestMethod]
    public void CanTrigger_FileDisplayContainerClass_ReturnsTrue()
    {
        var adapter = new DirectoryOpusInlineSearchAdapter();

        Assert.IsTrue(adapter.CanTrigger(SomeHwnd, "dopus.filedisplaycontainer"));
    }

    [TestMethod]
    public void CanTrigger_IconFileDisplayClass_ReturnsTrue()
    {
        // Thumbnails/Tiles/Large Icons view modes focus this class instead of "dopus.filedisplay" --
        // previously unrecognized, so inline search never triggered in those view modes.
        var adapter = new DirectoryOpusInlineSearchAdapter();

        Assert.IsTrue(adapter.CanTrigger(SomeHwnd, "dopus.iconfiledisplay"));
    }

    [TestMethod]
    public void CanTrigger_UnrelatedClass_ReturnsFalse()
    {
        var adapter = new DirectoryOpusInlineSearchAdapter();

        Assert.IsFalse(adapter.CanTrigger(SomeHwnd, "dopus.lister"));
    }

    [TestMethod]
    public void CanTrigger_ZeroHwnd_ReturnsFalse()
    {
        var adapter = new DirectoryOpusInlineSearchAdapter();

        Assert.IsFalse(adapter.CanTrigger(IntPtr.Zero, "dopus.filedisplay"));
    }

    [TestMethod]
    public void CanTrigger_EmptyClassName_ReturnsFalse()
    {
        var adapter = new DirectoryOpusInlineSearchAdapter();

        Assert.IsFalse(adapter.CanTrigger(SomeHwnd, ""));
    }
}
