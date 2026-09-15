namespace Lertaro.App.Helpers.App;

// Split out to keep App.xaml.cs below the repository's per-file line limit; this handler owns its
// re-entrancy state and has no reason to be coupled to the rest of the application bootstrap.
internal sealed class DispatcherExceptionHandler
{
    private int _crashReportDepth;

    public void Handle(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs args)
    {
        var isFirst = Interlocked.CompareExchange(ref _crashReportDepth, 1, 0) == 0;
        try
        {
            AppCrashHandler.LogException("DispatcherUnhandledException", args.Exception);
        }
        finally
        {
            if (isFirst)
                Interlocked.Exchange(ref _crashReportDepth, 0);
        }
        args.Handled = isFirst;
    }
}
