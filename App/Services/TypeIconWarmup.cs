using System.IO;

using Lertaro.App.Services.ShellIcons;

namespace Lertaro.App.Services;

/// <summary>
/// Resolves the icon of each result TYPE, in the background, so the first search of a session never has to
/// ask the shell for one on the UI thread.
/// </summary>
/// <remarks>
/// An ordinary row draws its extension's icon, and the binding asks for it on the UI thread. A cache miss
/// there resolves the icon synchronously (SHGetFileInfo against the shell's image list), which measured tens
/// of milliseconds per type -- and the first search of a session misses every type at once. That is why the
/// very first keystroke after a launch could not even echo: the UI thread was inside the shell, not painting.
///
/// The set is small and bounded (one entry per extension, a dozen or so), so warming it is cheap and it never
/// scales with how many files a search matches. Runs at below-normal priority, off the UI thread, and every
/// icon it resolves lands in the same cache the rows read.
/// </remarks>
internal static class TypeIconWarmup
{
    // The extensions a search is overwhelmingly likely to show. Anything else still resolves on demand; it
    // just resolves once and is cached from then on.
    private static readonly string[] Extensions =
    [
        "",           // directories, via the folder sentinel
        ".exe",
        ".lnk",
        ".txt",
        ".md",
        ".pdf",
        ".doc",
        ".docx",
        ".xls",
        ".xlsx",
        ".ppt",
        ".pptx",
        ".jpg",
        ".jpeg",
        ".png",
        ".gif",
        ".svg",
        ".mp3",
        ".wav",
        ".flac",
        ".mp4",
        ".mkv",
        ".avi",
        ".zip",
        ".rar",
        ".7z",
        ".json",
        ".xml",
        ".html",
        ".css",
        ".js",
        ".ts",
        ".cs",
        ".py",
        ".iso",
    ];

    public static void Schedule()
    {
        _ = Task.Factory.StartNew(
            () =>
            {
                try
                {
                    Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                }
                catch
                {
                }

                try
                {
                    // Directories first: a result list is usually half folders.
                    _ = "dummy_folder".GetIconForPath(isDir: true);

                    foreach (var extension in Extensions)
                    {
                        if (extension.Length == 0)
                            continue;

                        try
                        {
                            _ = ("dummy" + extension).GetIconForPath(isDir: false);
                        }
                        catch
                        {
                            // One unreadable type must not stop the rest.
                        }
                    }
                }
                catch
                {
                    // Warm-up is an optimisation; the on-demand path still works.
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }
}
