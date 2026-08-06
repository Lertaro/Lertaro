using System.Windows;
using Lertaro.App.Services;
using Lertaro.Core;
using MessageBox = Lertaro.App.Views.Controls.Dialogs.CustomMessageBox;

namespace Lertaro.App.Helpers.App;

/// <summary>
/// Handles critical crash logging and unhandled exception reporting.
/// ponytail: Split out purely to keep App.xaml.cs under the repo's 300-line limit.
/// </summary>
public static class AppCrashHandler
{
    public static void LogException(string source, Exception? ex)
    {
        var details = ex != null ? ex.ToString() : "Null exception object";
        Logger.Log($"CRITICAL CRASH ({source}):\n{details}", LogLevel.Error);
        MessageBox.Show(
            string.Format(TranslationManager.Instance["Crash_Message"], source, ex?.Message, Logger.LogDir),
            TranslationManager.Instance["Crash_Title"],
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
