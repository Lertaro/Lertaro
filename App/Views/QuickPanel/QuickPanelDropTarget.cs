using System.IO;
using System.Windows;
using Lertaro.App.ViewModels.QuickPanel;
using Lertaro.PluginSdk.Shell.FileOperations;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;

namespace Lertaro.App.Views.QuickPanel;

// Dropping files onto a group copies them into that group's folder. Split out of QuickPanelWindow so
// the window file stays under the repo's per-file line limit; it has no state of its own and works
// entirely off the group each handler's own DataContext names.
//
// The copy is Windows' own (IFileOperation, via ShellPasteHelper): one native progress dialog for the
// whole batch, the real "this file already exists" prompt, and an undo entry -- all things a hand-rolled
// File.Copy loop would have to reinvent badly.
public partial class QuickPanelWindow
{
    /// <summary>Whether a drag hovering over this group is one it should take.</summary>
    /// <remarks>
    /// Three separate questions, and all three have to be yes. Whether the source was configured as a
    /// drop target. Whether the drag is carrying files at all -- a drag of text or of anything else has
    /// nothing to copy. And whether it started inside this panel: a row dragged from one group towards
    /// another is on its way OUT to some other window, and turning a half-finished drag-out into a real
    /// file copy in the folder next door is not a mistake worth being able to make.
    /// </remarks>
    internal static bool CanDrop(QuickPanelGroupViewModel? group, bool carriesFiles, bool startedInsideThePanel)
        => group is { AcceptsDrops: true }
           && carriesFiles
           && !startedInsideThePanel
           && !string.IsNullOrEmpty(group.FolderPath)
           && Directory.Exists(group.FolderPath);

    // The three answers read off the live drag. IsDragActive is set by this app's own drag-out helper for
    // the length of its DoDragDrop, which is exactly "this drag started in one of our lists".
    //
    // "Carrying files" is two questions, because there are two ways to carry one. CF_HDROP is a drag of
    // things that already exist on disk. A browser dragging an image, or Outlook an attachment, has no
    // path to give and offers the bytes instead -- see VirtualFileExtractor.
    private static bool CanDrop(QuickPanelGroupViewModel? group, DragEventArgs e)
        => CanDrop(group,
            e.Data.GetDataPresent(DataFormats.FileDrop) || VirtualFileExtractor.HasVirtualFiles(e.Data),
            Controls.Results.ResultsDragDropHelper.IsDragActive);

    private static QuickPanelGroupViewModel? GroupOf(object sender)
        => (sender as FrameworkElement)?.DataContext as QuickPanelGroupViewModel;

    private void Group_DragOver(object sender, DragEventArgs e)
    {
        var group = GroupOf(sender);
        var allowed = CanDrop(group, e);

        // Copy, never Move, whatever the source offered or the user is holding down. The panel is
        // somewhere to put a copy of something; a drag into it that could silently take the original
        // away from wherever it came from is a different and much more expensive thing to get wrong.
        e.Effects = allowed ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        group?.IsDropTarget = allowed;
    }

    private void Group_DragLeave(object sender, DragEventArgs e)
    {
        if (GroupOf(sender) is { } group)
            group.IsDropTarget = false;
    }

    private void Group_Drop(object sender, DragEventArgs e)
    {
        var group = GroupOf(sender);
        group?.IsDropTarget = false;

        if (!CanDrop(group, e)) return;

        e.Handled = true;

        // Real paths if the drag has them, otherwise the bytes written out to somewhere they do have
        // one. Either way what reaches the copy below is a list of files on disk, so there is one path
        // through the shell rather than two.
        var staging = (string?)null;
        List<string> sources;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            sources = paths.Where(path => File.Exists(path) || Directory.Exists(path)).ToList();
        }
        else
        {
            staging = Path.Combine(Path.GetTempPath(), "Lertaro", "Drop", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            sources = VirtualFileExtractor.Extract(e.Data, staging);
        }

        if (sources.Count == 0)
        {
            TryDelete(staging);
            return;
        }

        // Copied rather than written straight into the folder, even when they were just written once
        // already. That second hop is what buys the native conflict prompt, the progress dialog and the
        // undo entry -- writing directly would land the file and offer none of the three.
        //
        // Never a move, so the flag is fixed rather than read from the drag. See Group_DragOver.
        //
        // Nothing here asks for a reload. The group's folder is being watched for as long as the panel
        // is open, so the files landing IS the notification -- from whoever put them there, by whatever
        // means. The callback is only for sweeping up the staging folder, once the copy that reads from
        // it is done with it.
        ShellPasteHelper.PasteAsync(sources, group!.FolderPath, move: false,
            onCompleted: () => TryDelete(staging));
    }

    /// <summary>Removes the staging folder, if there was one. Never worth failing a drop over.</summary>
    private static void TryDelete(string? folder)
    {
        if (folder == null) return;

        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex)
        {
            // Under the user's own temp folder, so what is left behind is swept up with everything else
            // there. Worth a line, not worth an error.
            Core.Logger.Log($"[QuickPanel] Could not remove the drop staging folder: {ex.Message}", Core.LogLevel.Debug);
        }
    }
}
