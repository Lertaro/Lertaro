namespace Lertaro.Plugins.WPS.Tests;

// The name matching that decides whether a window is even worth a UI Automation call. Everything past
// this point needs a live WPS dialog, so this is the part that can be pinned.
[TestClass]
public sealed class WPSDialogIdentityTests
{
    [TestMethod]
    public void AllFourWPSExecutablesAreRecognised()
    {
        // Writer, Spreadsheets, Presentation and the PDF reader are separate processes, and the dialog
        // belongs to whichever one opened it. Missing any of them means the integration silently does
        // nothing in that app.
        Assert.IsTrue(WPSDialogIdentity.IsWPSProcess("wps"));
        Assert.IsTrue(WPSDialogIdentity.IsWPSProcess("et"));
        Assert.IsTrue(WPSDialogIdentity.IsWPSProcess("wpp"));
        Assert.IsTrue(WPSDialogIdentity.IsWPSProcess("wpspdf"));
    }

    [TestMethod]
    public void ProcessNameMatchingIgnoresCase()
    {
        // Process.ProcessName reports whatever casing the executable was registered with.
        Assert.IsTrue(WPSDialogIdentity.IsWPSProcess("WPS"));
        Assert.IsTrue(WPSDialogIdentity.IsWPSProcess("Et"));
    }

    [TestMethod]
    public void ATrailingExeIsTolerated()
    {
        // The callers pass Process.ProcessName, which has no extension, but a caller holding a file name
        // instead should not silently fail to match.
        Assert.IsTrue(WPSDialogIdentity.IsWPSProcess("wps.exe"));
        Assert.IsTrue(WPSDialogIdentity.IsWPSProcess("WPSPDF.EXE"));
    }

    [TestMethod]
    public void UnrelatedProcessesAreRejected()
    {
        Assert.IsFalse(WPSDialogIdentity.IsWPSProcess("explorer"));
        Assert.IsFalse(WPSDialogIdentity.IsWPSProcess("WINWORD"));
        Assert.IsFalse(WPSDialogIdentity.IsWPSProcess("notepad.exe"));
    }

    [TestMethod]
    public void AProcessMerelyContainingAWPSNameIsRejected()
    {
        // Substring matching here would drag in unrelated processes -- "et" in particular is two letters
        // and appears inside plenty of executable names.
        Assert.IsFalse(WPSDialogIdentity.IsWPSProcess("dotnet"));
        Assert.IsFalse(WPSDialogIdentity.IsWPSProcess("telnet"));
        Assert.IsFalse(WPSDialogIdentity.IsWPSProcess("wpsoffice"));
    }

    [TestMethod]
    public void MissingProcessNamesAreRejected()
    {
        Assert.IsFalse(WPSDialogIdentity.IsWPSProcess(null));
        Assert.IsFalse(WPSDialogIdentity.IsWPSProcess(""));
        Assert.IsFalse(WPSDialogIdentity.IsWPSProcess("   "));
    }

    [TestMethod]
    public void TheDialogClassNameIsMatchedExactly()
    {
        // Case-sensitive on purpose: this is a Qt widget class reported by UI Automation, not a Win32
        // window class, and it is spelled one way.
        Assert.IsTrue(WPSDialogIdentity.IsWPSDialogClassName("KcfdFileDialog"));
        Assert.IsFalse(WPSDialogIdentity.IsWPSDialogClassName("kcfdfiledialog"));
        Assert.IsFalse(WPSDialogIdentity.IsWPSDialogClassName("KcfdFileDialogEx"));
        Assert.IsFalse(WPSDialogIdentity.IsWPSDialogClassName(null));
    }

    [TestMethod]
    public void AQtWindowClassIsNotMistakenForTheDialog()
    {
        // What Win32 GetClassName actually returns for these windows -- and WPS's ordinary document
        // windows carry it too, so matching on it would treat every WPS window as a file dialog. The
        // whole reason the class test goes through UI Automation instead.
        Assert.IsFalse(WPSDialogIdentity.IsWPSDialogClassName("Qt5QWindowIcon"));
        Assert.IsFalse(WPSDialogIdentity.IsWPSDialogClassName("#32770"));
    }

    [TestMethod]
    public void TheWin32PreFilterAcceptsEveryFormOfTheQtWindowClass()
    {
        // Digits vary with the Qt build, and Sandboxie prefixes the whole class with "Sandbox:BoxName:"
        // -- the middle two were read off a live WPS running under it. Only "QWindowIcon" is stable.
        Assert.IsTrue(WPSDialogIdentity.CouldBeDialogWindowClass("Qt5QWindowIcon"));
        Assert.IsTrue(WPSDialogIdentity.CouldBeDialogWindowClass("Sandbox:DefaultBox:Qt5QWindowIcon"));
        Assert.IsTrue(WPSDialogIdentity.CouldBeDialogWindowClass("Qt5152QWindowIcon"));
        Assert.IsTrue(WPSDialogIdentity.CouldBeDialogWindowClass("Qt6QWindowIcon"));
    }

    [TestMethod]
    public void TheWin32PreFilterRejectsWPSsOtherWindowsForFree()
    {
        // The point of the pre-filter: these never reach the cross-process check. Both class names were
        // read off a live WPS -- the main window and the frame it draws around itself.
        Assert.IsFalse(WPSDialogIdentity.CouldBeDialogWindowClass("Sandbox:DefaultBox:OpusApp"));
        Assert.IsFalse(WPSDialogIdentity.CouldBeDialogWindowClass("OpusApp"));
        Assert.IsFalse(WPSDialogIdentity.CouldBeDialogWindowClass("Sandbox:DefaultBox:KLiteMainWindowShadowBorder"));
        Assert.IsFalse(WPSDialogIdentity.CouldBeDialogWindowClass("#32770"));
        Assert.IsFalse(WPSDialogIdentity.CouldBeDialogWindowClass(""));
        Assert.IsFalse(WPSDialogIdentity.CouldBeDialogWindowClass(null));
    }
}
