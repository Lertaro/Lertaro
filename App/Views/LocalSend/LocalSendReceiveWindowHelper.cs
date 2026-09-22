using System.IO;
using System.Windows.Controls;
using Lertaro.App.Services;
using Lertaro.Core.Services.LocalSend;

namespace Lertaro.App.Views.LocalSend;

/// <summary>
/// UI helper methods for LocalSendReceiveWindow.xaml.cs.
/// ponytail: Split out purely to keep LocalSendReceiveWindow.xaml.cs under the repo's 300-line limit.
/// </summary>
public static class LocalSendReceiveWindowHelper
{
    public static string FormatSummaryFileName(string firstFileName, int totalFiles)
    {
        if (string.IsNullOrEmpty(firstFileName)) return string.Empty;
        return totalFiles > 1 ? $"{firstFileName} ({totalFiles})" : firstFileName;
    }

    /// <summary>
    /// Which path the reveal-in-Explorer button targets: the folder the session wrote into, and failing
    /// that the file that actually landed. The root is preferred because a multi-file receive puts every
    /// file under it; falling through matters when it is not there -- a folder the sender named but never
    /// sent anything into, or one an earlier build derived from the server default rather than from the
    /// session's own *Save To…* directory. Returning empty hid the button in both cases.
    /// </summary>
    public static string ResolveFolderTarget(string? rootPath, string? savedPath)
    {
        if (!string.IsNullOrEmpty(rootPath) && Directory.Exists(rootPath)) return rootPath;
        if (!string.IsNullOrEmpty(savedPath) && (File.Exists(savedPath) || Directory.Exists(savedPath))) return savedPath;
        return string.Empty;
    }

    public static double UpdateItemProgress(ItemCollection items, LocalSendProgressArgs args)
    {
        var totalSessionBytes = args.SessionTotalBytes > 0 ? args.SessionTotalBytes : args.TotalBytes;
        var transferredSessionBytes = args.SessionBytesTransferred > 0 ? args.SessionBytesTransferred : args.BytesTransferred;
        var sessionPercentage = totalSessionBytes > 0 ? Math.Clamp((double)transferredSessionBytes / totalSessionBytes * 100.0, 0, 100) : 0;

        UpdateItems(items.OfType<LocalSendReceiveFileItem>(), args,
            TranslationManager.Instance["Settings_LocalSend_Completed"], TranslationManager.Instance["Local_StateFailed"]);
        return sessionPercentage;
    }

    internal static void UpdateItems(IEnumerable<LocalSendReceiveFileItem> items, LocalSendProgressArgs args,
        string completedText, string failedText)
    {
        foreach (var item in items)
        {
            item.ShowProgress = true;
            if (item.FileId == args.FileId)
            {
                item.IsCanceled = false;
                item.IsFailed = args.IsFailed;
                item.IsFinished = args.IsFinished;
                var pct = args.TotalBytes > 0 ? (double)args.BytesTransferred / args.TotalBytes * 100.0 : 0;
                item.ProgressPercentage = Math.Min(100.0, pct);
                if (args.IsFailed)
                {
                    item.ProgressPercentage = 100.0;
                    item.StatusText = failedText;
                }
                else if (args.IsFinished)
                {
                    item.ProgressPercentage = 100.0;
                    item.StatusText = completedText;
                }
                else
                {
                    item.StatusText = $"{item.ProgressPercentage:F0}%";
                }
            }
            else if (item.IsFinished || (args.IsAllDone && !item.IsFailed))
            {
                item.IsFinished = true;
                item.ProgressPercentage = 100.0;
                item.StatusText = completedText;
            }
        }
    }

    public static void UpdateItemLanguage(IEnumerable<LocalSendReceiveFileItem> items)
    {
        foreach (var item in items)
        {
            if (item.IsFailed)
            {
                item.StatusText = TranslationManager.Instance["Local_StateFailed"];
            }
            else if (item.IsFinished)
            {
                item.StatusText = TranslationManager.Instance["Settings_LocalSend_Completed"];
            }
            else if (item.IsCanceled)
            {
                item.StatusText = TranslationManager.Instance["Settings_LocalSend_Canceled"];
            }
        }
    }

    public static void MarkCanceledItems(IEnumerable<LocalSendReceiveFileItem> items, string canceledText)
    {
        foreach (var item in items.Where(item => !item.IsFinished))
        {
            item.IsCanceled = true;
            item.StatusText = canceledText;
            item.ShowProgress = false;
        }
    }
}
