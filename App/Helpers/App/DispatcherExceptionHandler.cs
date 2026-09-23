namespace Lertaro.App.Helpers.App;

// Split out to keep App.xaml.cs below the repository's per-file line limit; this handler owns its
// re-entrancy state and has no reason to be coupled to the rest of the application bootstrap.
internal sealed class DispatcherExceptionHandler
{
    internal const string FirstReportSource = "DispatcherUnhandledException";
    internal const string ReentrantReportSource = "DispatcherUnhandledException (during the crash dialog)";

    private int _crashReportDepth;
    private readonly Action<string, Exception?, bool> _report;

    /// <summary>
    /// Test seam for the reporting itself: the dialog half of
    /// <see cref="AppCrashHandler.LogException"/> needs a live <see cref="System.Windows.Application"/>.
    /// </summary>
    public DispatcherExceptionHandler(Action<string, Exception?, bool>? report = null) =>
        _report = report ?? AppCrashHandler.LogException;

    public void Handle(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs args)
    {
        OnUnhandledException(args.Exception);
        args.Handled = true;
    }

    // Split out because DispatcherUnhandledExceptionEventArgs has no public constructor, so the decision
    // below is otherwise unreachable from a test.
    internal void OnUnhandledException(Exception exception)
    {
        // Reporting shows a modal, and a modal pumps the dispatcher queue: any other queued operation that
        // fails while the dialog is up comes back through here from inside ShowDialog. Letting that one
        // report with a dialog of its own is what a user sees as the crash dialog never appearing -- the
        // nested exception unwinds the outer ShowDialog, and AppCrashHandler's own catch then logs
        // "crash dialog failed" over the report that was about to be read. So a second, nested crash is
        // logged and swallowed, which is also what lets the first dialog finish.
        if (Interlocked.CompareExchange(ref _crashReportDepth, 1, 0) != 0)
        {
            _report(ReentrantReportSource, exception, false);
            return;
        }

        try
        {
            _report(FirstReportSource, exception, true);
        }
        finally
        {
            Interlocked.Exchange(ref _crashReportDepth, 0);
        }
    }
}
