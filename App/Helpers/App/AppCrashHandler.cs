using System.Windows;
using Lertaro.App.Services;
using Lertaro.Core;
using Application = System.Windows.Application;
using MessageBox = Lertaro.App.Views.Controls.Dialogs.CustomMessageBox;

namespace Lertaro.App.Helpers.App;

/// <summary>
/// Handles critical crash logging and unhandled exception reporting.
/// ponytail: Split out purely to keep App.xaml.cs under the repo's 300-line limit.
/// </summary>
public static class AppCrashHandler
{
    public static void LogException(string source, Exception? ex, bool showDialog = true)
    {
        var details = ex != null ? ex.ToString() : "Null exception object";
        Logger.Log($"CRITICAL CRASH ({source}):\n{details}", LogLevel.Error);
        if (!showDialog)
            return;

        // Report without ever waiting for the UI thread FROM another one. An unobserved task exception
        // arrives here from the finalizer thread, and CustomMessageBox.Show would then block that thread
        // on a Dispatcher operation -- which the UI thread can only serve by pumping, and it was not
        // pumping because trimming its working set waits for pending finalizers. Each thread held the
        // other, with the dialog still unscheduled.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => ShowDialog(source, ex)));
            return;
        }
        ShowDialog(source, ex);
    }

    private static void ShowDialog(string source, Exception? ex)
    {
        try
        {
            MessageBox.Show(
                string.Format(TranslationManager.Instance["Crash_Message"], source, ex?.Message, Logger.LogDir),
                TranslationManager.Instance["Crash_Title"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception dialogEx)
        {
            // The crash dialog itself failed -- e.g. the crash originated in the dialog or
            // translation path it depends on. Reporting must never escalate into another
            // unhandled exception riding on a UI that is already broken.
            Logger.Log($"CRITICAL CRASH ({source}): crash dialog failed: {dialogEx}", LogLevel.Error);
        }
    }
}
