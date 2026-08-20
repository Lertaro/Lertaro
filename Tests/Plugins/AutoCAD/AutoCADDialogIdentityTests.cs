namespace Lertaro.Plugins.AutoCAD.Tests;

[TestClass]
public sealed class AutoCADDialogIdentityTests
{
    [TestMethod]
    public void RecognizesAutoCADExecutables()
    {
        Assert.IsTrue(AutoCADDialogIdentity.IsAutoCADProcess("acad"));
        Assert.IsTrue(AutoCADDialogIdentity.IsAutoCADProcess("acad.exe"));
        Assert.IsTrue(AutoCADDialogIdentity.IsAutoCADProcess("ACADLT"));
    }

    [TestMethod]
    public void RejectsUnrelatedProcesses()
    {
        Assert.IsFalse(AutoCADDialogIdentity.IsAutoCADProcess("explorer"));
        Assert.IsFalse(AutoCADDialogIdentity.IsAutoCADProcess("acadhelper"));
        Assert.IsFalse(AutoCADDialogIdentity.IsAutoCADProcess(null));
    }

    [TestMethod]
    public void MatchesOnlyCommonDialogClass()
    {
        Assert.IsTrue(AutoCADDialogIdentity.IsCommonDialog("#32770"));
        Assert.IsFalse(AutoCADDialogIdentity.IsCommonDialog("AutoCAD"));
    }
}
