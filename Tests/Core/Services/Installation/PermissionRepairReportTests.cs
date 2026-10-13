using Lertaro.Core.Services.Installation;

namespace Lertaro.Core.Tests.Services.Installation;

// The channel an elevated, hidden, log-less repair leaves its trace in (issue #342). Everything here runs
// against an explicit path, so no test touches the real %TEMP%\Lertaro\repair.log.
[TestClass]
public sealed class PermissionRepairReportTests
{
    private readonly string _directory = Directory.CreateTempSubdirectory("LertaroRepairReport-").FullName;
    private string LogPath => Path.Combine(_directory, "nested", "repair.log");

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_directory, true); } catch { }
    }

    [TestMethod]
    public void Write_CreatesTheDirectoryAndAppendsEveryStep()
    {
        PermissionRepairReport.Write("repair starting", LogPath);
        PermissionRepairReport.Write("repairing C:\\Users\\x\\AppData\\Local\\Lertaro", LogPath);

        var report = PermissionRepairReport.Read(LogPath);

        Assert.IsNotNull(report);
        StringAssert.Contains(report, "repair starting");
        StringAssert.Contains(report, "repairing C:\\Users\\x\\AppData\\Local\\Lertaro");
        // No BOM: another process reads this back and may show it verbatim.
        Assert.AreNotEqual(0xEF, File.ReadAllBytes(LogPath)[0]);
    }

    [TestMethod]
    public void Clear_DropsThePreviousRunsTrace()
    {
        PermissionRepairReport.Write("from the previous attempt", LogPath);
        PermissionRepairReport.Clear(LogPath);
        PermissionRepairReport.Write("from this attempt", LogPath);

        var report = PermissionRepairReport.Read(LogPath);

        Assert.IsNotNull(report);
        Assert.IsFalse(report.Contains("previous attempt", StringComparison.Ordinal), report);
    }

    // A repair killed for running too long cannot write a final line, so the step it was ON is what the last
    // line has to be -- that is the whole reason each step is recorded before it runs.
    [TestMethod]
    public void LastStep_OfATraceThatWasCutOff_IsTheStepItWasOn()
    {
        PermissionRepairReport.Write("locking the shared data and application directories", LogPath);
        PermissionRepairReport.Write("repairing C:\\Users\\x\\AppData\\Local\\Lertaro", LogPath);

        var last = PermissionRepairReport.LastStep(PermissionRepairReport.Read(LogPath));

        Assert.IsNotNull(last);
        StringAssert.Contains(last, "repairing C:\\Users\\x");
    }

    [TestMethod]
    public void Read_NoTraceAtAll_IsNull()
    {
        Assert.IsNull(PermissionRepairReport.Read(LogPath));
        Assert.IsNull(PermissionRepairReport.LastStep(null));
    }

    [TestMethod]
    public void Read_EmptyTrace_IsNullRatherThanBlank()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        File.WriteAllText(LogPath, "   \n");

        Assert.IsNull(PermissionRepairReport.Read(LogPath));
    }

    // The file lives at a path any local process can write to, so an oversized one is started over rather than
    // appended to: a broken repair must not be able to fill the user's temp folder through this channel.
    [TestMethod]
    public void Write_AnOversizedTrace_StartsOver()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        File.WriteAllText(LogPath, new string('x', 32 * 1024));

        PermissionRepairReport.Write("repair starting", LogPath);

        var report = PermissionRepairReport.Read(LogPath);
        Assert.IsNotNull(report);
        Assert.IsTrue(report.Length < 32 * 1024, report.Length.ToString());
        StringAssert.Contains(report, "repair starting");
    }
}
