using System.Collections.ObjectModel;
using System.IO;
using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.App.ViewModels.LocalSend;

/// <summary>Builds the visible send list from the same directory-relative paths used by LocalSendClient.</summary>
internal sealed class LocalSendSendProgressTracker
{
    internal ObservableCollection<LocalSendSendFileItem> Items { get; } = [];
    internal int ConfirmedCount => Items.Count(item => item.IsConfirmed);

    internal void PrepareFiles(IEnumerable<string> paths)
    {
        Items.Clear();
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                Add(path, Path.GetFileName(path));
            }
            else if (Directory.Exists(path))
            {
                var fullPath = Path.GetFullPath(path);
                var parent = Path.GetDirectoryName(fullPath);
                var baseDirectory = string.IsNullOrEmpty(parent) ? fullPath : parent;
                try
                {
                    foreach (var file in Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories))
                        Add(file, Path.GetRelativePath(baseDirectory, file).Replace('\\', '/'));
                }
                catch { }
            }
        }
    }

    internal void PrepareText(string text, string displayName)
    {
        Items.Clear();
        Items.Add(new LocalSendSendFileItem { DisplayName = displayName, Size = text.Length, SizeText = string.Empty });
    }

    internal void UpdateProgress(LocalSendSendProgressArgs args, string waitingText)
    {
        if (!TryGet(args.FileIndex, out var item)) return;
        item.ShowProgress = true;
        item.ProgressPercentage = args.TotalBytes > 0 ? Math.Min(99, (double)args.BytesSent / args.TotalBytes * 100) : 0;
        item.StatusText = item.ProgressPercentage >= 99 ? waitingText : $"{item.ProgressPercentage:F0}%";
    }

    internal void MarkConfirmed(LocalSendFileConfirmationArgs args, string completedText)
    {
        if (!TryGet(args.FileIndex, out var item)) return;
        item.ShowProgress = true;
        item.ProgressPercentage = 100;
        item.IsConfirmed = true;
        item.StatusText = completedText;
    }

    internal void MarkFailed(LocalSendFileConfirmationArgs args, string failureText)
    {
        if (!TryGet(args.FileIndex, out var item)) return;
        item.ShowProgress = true;
        item.StatusText = failureText;
    }

    internal void MarkPending(string statusText)
    {
        foreach (var item in Items.Where(item => !item.IsConfirmed))
        {
            item.ShowProgress = true;
            item.StatusText = statusText;
        }
    }

    private void Add(string path, string displayName)
    {
        var size = new FileInfo(path).Length;
        Items.Add(new LocalSendSendFileItem { DisplayName = displayName, Size = size, SizeText = LocalSendServerHelper.FormatBytes(size) });
    }

    private bool TryGet(int index, out LocalSendSendFileItem item)
    {
        var itemIndex = index - 1;
        if (itemIndex >= 0 && itemIndex < Items.Count)
        {
            item = Items[itemIndex];
            return true;
        }

        item = null!;
        return false;
    }
}
