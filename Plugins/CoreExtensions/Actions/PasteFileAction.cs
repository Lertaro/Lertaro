using System.Windows.Media;
using Lertaro.PluginSdk;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Helpers;

using Lertaro.PluginSdk.Shell.FileOperations;
namespace Lertaro.Plugins.CoreExtensions.Actions;

public class PasteFileAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_BuiltinGroup");

    public string DisplayName => TranslationService.Get("Action_Paste");

    public string Description => TranslationService.Get("Action_Paste_Desc");

    // No default hotkey, deliberately -- it used to be Ctrl+V. CanExecute does keep it out of the way
    // of pasting text (it needs a real file-drop-list on the clipboard), but that only bounds WHEN it
    // fires, not what it does when it does: writing files into whichever folder the selected result
    // happens to sit in. Users reported that landing by accident. Bindable in Settings for anyone who
    // wants it.
    public string Hotkey => string.Empty;

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M19 2h-4.18C14.4.84 13.3 0 12 0c-1.3 0-2.4.84-2.82 2H5c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm-7 0c.55 0 1 .45 1 1s-.45 1-1 1-1-.45-1-1 .45-1 1-1zm7 18H5V4h2v3h10V4h2v16z",
        "TextPrimary");

    // Offered on any existing file or folder -- a file target pastes into its parent directory (same
    // as Explorer's own "paste here" when a file is selected) -- and only when the clipboard actually
    // carries a file list, matching Explorer's own Paste, which never shows as a dead/no-op item either.
    // A Cut additionally requires all selected targets to resolve to the SAME destination folder: the
    // source files only exist at their original location once, so moving them into more than one
    // destination is impossible (the second move would find nothing left to move).
    public bool CanExecute(IReadOnlyList<ISearchResult> results)
    {
        if (results.Count == 0 || !results.All(Exists) || !HasFileClipboard()) return false;
        if (!IsMoveEffect()) return true;
        return GetDistinctDestinations(results).Length == 1;
    }

    private static bool Exists(ISearchResult result)
    {
        if (result == null || string.IsNullOrEmpty(result.FullPath)) return false;
        return PathExistenceCache.Exists(result.FullPath);
    }

    private static string? GetDestinationFolder(ISearchResult result) =>
        result.IsDir ? result.FullPath : System.IO.Path.GetDirectoryName(result.FullPath);

    // Dedupe destinations: multiple selected files sharing one parent folder (or a file selected
    // alongside its own parent folder) would otherwise queue the same paste twice into that folder.
    private static string[] GetDistinctDestinations(IReadOnlyList<ISearchResult> results) =>
        results
            .Where(Exists)
            .Select(GetDestinationFolder)
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct()
            .ToArray()!;

    private static bool HasFileClipboard()
    {
        try { return System.Windows.Clipboard.ContainsFileDropList(); }
        catch { return false; }
    }

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view)
    {
        System.Collections.Specialized.StringCollection? files;
        try
        {
            files = System.Windows.Clipboard.GetFileDropList();
        }
        catch (Exception ex)
        {
            Logger.Log($"[PasteFileAction] Failed to read clipboard: {ex.Message}", LogLevel.Error);
            return;
        }

        if (files == null || files.Count == 0) return;

        var sourcePaths = files.Cast<string>().ToArray();
        var move = IsMoveEffect();
        var destinations = GetDistinctDestinations(results);

        // Defensive mirror of the CanExecute gate above: even if Execute were somehow invoked without
        // that check, never fan a move out to more than one destination.
        if (move) destinations = destinations.Take(1).ToArray();

        foreach (var destination in destinations)
            ShellPasteHelper.PasteAsync(sourcePaths, destination, move);
    }

    // The same "Preferred DropEffect" marker CutFileAction writes (DragDropEffects.Move) -- absent, or
    // set to anything else, means Copy, matching Explorer's own Ctrl+V semantics.
    private static bool IsMoveEffect()
    {
        try
        {
            if (System.Windows.Clipboard.GetDataObject()?.GetData("Preferred DropEffect") is System.IO.MemoryStream stream)
            {
                var buffer = new byte[4];
                stream.Position = 0;
                if (stream.Read(buffer, 0, 4) == 4)
                {
                    var effect = (System.Windows.DragDropEffects)BitConverter.ToInt32(buffer, 0);
                    return (effect & System.Windows.DragDropEffects.Move) != 0;
                }
            }
        }
        catch { }
        return false;
    }
}
