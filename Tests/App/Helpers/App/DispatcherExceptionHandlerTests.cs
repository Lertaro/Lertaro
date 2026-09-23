using Lertaro.App.Helpers.App;

namespace Lertaro.App.Tests.Helpers.App;

// Which crash gets to put a dialog on screen. Showing one is AppCrashHandler's half and needs a live
// Application; what is owned here is the re-entrancy decision wrapped around it.
[TestClass]
public sealed class DispatcherExceptionHandlerTests
{
    private sealed record Report(string Source, Exception? Exception, bool ShowDialog);

    [TestMethod]
    public void OnUnhandledException_FirstCrashReportsWithADialog()
    {
        var reports = new List<Report>();
        var handler = new DispatcherExceptionHandler((source, ex, dialog) => reports.Add(new Report(source, ex, dialog)));

        handler.OnUnhandledException(new InvalidOperationException("first"));

        Assert.HasCount(1, reports);
        Assert.AreEqual(DispatcherExceptionHandler.FirstReportSource, reports[0].Source);
        Assert.IsTrue(reports[0].ShowDialog);
    }

    [TestMethod]
    public void OnUnhandledException_CrashRaisedFromInsideTheDialogDoesNotOpenAnotherOne()
    {
        // What the modal pump actually does: reporting does not return until the user dismisses the dialog,
        // and while it is up another queued operation can fail and come straight back in here.
        var reports = new List<Report>();
        DispatcherExceptionHandler? handler = null;
        handler = new DispatcherExceptionHandler((source, ex, dialog) =>
        {
            reports.Add(new Report(source, ex, dialog));
            if (dialog)
                handler!.OnUnhandledException(new InvalidOperationException("nested"));
        });

        handler.OnUnhandledException(new InvalidOperationException("outer"));

        Assert.HasCount(2, reports);
        Assert.IsTrue(reports[0].ShowDialog, "the first crash is the one that gets the dialog");
        Assert.IsFalse(reports[1].ShowDialog, "a crash during the dialog must not nest another modal");
        Assert.AreEqual(DispatcherExceptionHandler.ReentrantReportSource, reports[1].Source);
        Assert.AreEqual("nested", reports[1].Exception!.Message);
    }

    [TestMethod]
    public void OnUnhandledException_AfterTheDialogClosedTheNextCrashReportsAgain()
    {
        var reports = new List<Report>();
        var handler = new DispatcherExceptionHandler((source, ex, dialog) => reports.Add(new Report(source, ex, dialog)));

        handler.OnUnhandledException(new InvalidOperationException("first"));
        handler.OnUnhandledException(new InvalidOperationException("second"));

        Assert.HasCount(2, reports);
        Assert.IsTrue(reports[1].ShowDialog, "the depth flag stayed latched after the report finished");
    }

    [TestMethod]
    public void OnUnhandledException_ReportingThatThrowsStillUnlatchesTheDepthFlag()
    {
        // The dialog can fail for its own reasons (it renders through translation and theming). That must
        // not cost every later crash its report, which is what a latched flag would do.
        var calls = 0;
        var handler = new DispatcherExceptionHandler((_, _, _) =>
        {
            if (++calls == 1)
                throw new InvalidOperationException("the dialog broke");
        });

        Assert.Throws<InvalidOperationException>(() => handler.OnUnhandledException(new Exception("first")));
        handler.OnUnhandledException(new Exception("second"));

        Assert.AreEqual(2, calls, "a handler still holding the previous crash's depth reports nothing at all");
    }
}
