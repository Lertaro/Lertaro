using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

// What the user is told when the elevated repair does not succeed. Issue #342 is the reason this is a function
// at all: one sentence covered a timeout, a refused lock walk, an unknown profile and a thrown exception, and
// the reporter had no way to tell which.
[TestClass]
public sealed class ServiceInstallManagerTests
{
    [TestMethod]
    public void DescribeRepairFailure_NonZeroExit_QuotesTheRepairsOwnDiagnostic()
    {
        var text = ServiceInstallManager.DescribeRepairFailure(
            exited: true, exitCode: 1,
            report: "repair failed: C:\\Users\\x\\AppData\\Local\\Lertaro: owner is still S-1-5-32-544, expected S-1-5-21-1",
            sid: "S-1-5-21-1");

        StringAssert.Contains(text, "exit code 1");
        StringAssert.Contains(text, "owner is still S-1-5-32-544");
        StringAssert.Contains(text, "--repair-permissions S-1-5-21-1");
    }

    [TestMethod]
    public void DescribeRepairFailure_Timeout_NamesTheStepItWasStuckOn()
    {
        var text = ServiceInstallManager.DescribeRepairFailure(
            exited: false, exitCode: 0,
            report: "[2026-10-07 21:59:30.000] [1234] locking the shared data and application directories",
            sid: "S-1-5-21-1");

        StringAssert.Contains(text, "did not finish within 120 seconds");
        StringAssert.Contains(text, "locking the shared data and application directories");
    }

    // The repair can die before writing anything at all -- then saying so is the honest answer, not silence.
    [TestMethod]
    public void DescribeRepairFailure_WithoutAnyTrace_SaysSoRatherThanLeavingItBlank()
    {
        var withoutTrace = ServiceInstallManager.DescribeRepairFailure(exited: true, exitCode: 2, report: null, sid: "S-1-5-21-1");
        var timedOut = ServiceInstallManager.DescribeRepairFailure(exited: false, exitCode: 0, report: null, sid: "S-1-5-21-1");

        StringAssert.Contains(withoutTrace, "no diagnostic of its own");
        StringAssert.Contains(timedOut, "unrecorded step");
    }

    // The trace is a file the user can open; a message box is not the place to show sixteen kilobytes of it.
    [TestMethod]
    public void DescribeRepairFailure_LongTrace_ShowsTheEndOfItAndPointsAtTheFile()
    {
        var trace = string.Join("\n", Enumerable.Range(1, 40).Select(index => $"[step {index}] doing something"));
        var text = ServiceInstallManager.DescribeRepairFailure(exited: true, exitCode: 1, report: trace, sid: "S-1-5-21-1");

        StringAssert.Contains(text, "[step 40]");
        Assert.IsFalse(text.Contains("[step 1] doing", StringComparison.Ordinal), text);
        StringAssert.Contains(text, "repair.log");
    }

    [TestMethod]
    public void TailOf_KeepsTheLastLinesOnly()
    {
        Assert.IsNull(ServiceInstallManager.TailOf(null));
        Assert.IsNull(ServiceInstallManager.TailOf("   "));

        var tail = ServiceInstallManager.TailOf("a\nb\nc\nd", lines: 2);

        Assert.AreEqual("c\nd", tail);
    }

    // The same failure shape as the repair, on the path that produces "the service never starts" with no other
    // trace at all: SilentInstall used to report a single generic sentence for a declined prompt, a timeout and
    // a failed lock walk alike.
    [TestMethod]
    public void DescribeInstallFailure_NonZeroExit_QuotesTheInstallersOwnDiagnostic()
    {
        var text = ServiceInstallManager.DescribeInstallFailure(
            exited: true, exitCode: 1,
            report: "installing from C:\\Soft\\Lertaro-Portable\\Lertaro\\Lertaro.Service.exe\n"
                    + "locking C:\\ProgramData\\Lertaro\n"
                    + "install failed: Could not lock the portable folder: C:\\Soft\\Lertaro-Portable\\Lertaro\\Plugins: Could not set the owner and permissions.",
            serviceExePath: @"C:\Soft\Lertaro-Portable\Lertaro\Lertaro.Service.exe");

        StringAssert.Contains(text, "exit code 1");
        StringAssert.Contains(text, "Could not set the owner and permissions");
        StringAssert.Contains(text, "--install");
    }

    [TestMethod]
    public void DescribeInstallFailure_Timeout_NamesTheStepItWasStuckOn()
    {
        var text = ServiceInstallManager.DescribeInstallFailure(
            exited: false, exitCode: 0,
            report: "[2026-10-22 14:03:24.100] [9004] locking C:\\ProgramData\\Lertaro",
            serviceExePath: @"C:\Soft\Lertaro-Portable\Lertaro\Lertaro.Service.exe");

        StringAssert.Contains(text, "did not finish within 120 seconds");
        StringAssert.Contains(text, @"locking C:\ProgramData\Lertaro");
    }

    // Nothing written at all is the case that produced the report this exists for: the process died before its
    // first line, so saying that is the only honest answer.
    [TestMethod]
    public void DescribeInstallFailure_WithoutAnyTrace_SaysSoRatherThanLeavingItBlank()
    {
        var withoutTrace = ServiceInstallManager.DescribeInstallFailure(exited: true, exitCode: 1, report: null,
            serviceExePath: @"C:\Soft\Lertaro\Lertaro.Service.exe");
        var timedOut = ServiceInstallManager.DescribeInstallFailure(exited: false, exitCode: 0, report: "  ",
            serviceExePath: @"C:\Soft\Lertaro\Lertaro.Service.exe");

        StringAssert.Contains(withoutTrace, "no diagnostic of its own");
        StringAssert.Contains(timedOut, "unrecorded step");
    }

    [TestMethod]
    public void DescribeInstallFailure_LongTrace_ShowsTheEndOfItAndPointsAtTheFile()
    {
        var trace = string.Join("\n", Enumerable.Range(1, 40).Select(index => $"[step {index}] doing something"));
        var text = ServiceInstallManager.DescribeInstallFailure(exited: true, exitCode: 1, report: trace,
            serviceExePath: @"C:\Soft\Lertaro\Lertaro.Service.exe");

        StringAssert.Contains(text, "[step 40]");
        Assert.IsFalse(text.Contains("[step 1] doing", StringComparison.Ordinal), text);
        StringAssert.Contains(text, "repair.log");
    }
}
