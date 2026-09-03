using System.Runtime.CompilerServices;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.Plugins.ContentSearch.Tests.TestSupport;

/// <summary>
/// Test stand-in for the App host's DirectoryIndexerService enumeration hook: walks the real
/// filesystem the same way the host's fallback live walk does. Scheduler tests run without the
/// App process, so without this delegate host enumeration would silently return nothing.
/// </summary>
internal static class LiveDirectoryEnumerator
{
    public static async IAsyncEnumerable<ISearchResult> EnumerateAsync(
        string folder,
        bool recursive,
        string filterPattern,
        int limit,
        [EnumeratorCancellation] CancellationToken token)
    {
        if (!Directory.Exists(folder))
            yield break;

        var files = Directory.EnumerateFiles(
            folder,
            "*",
            recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            yield return new LiveSearchResult(info);
            if (limit > 0)
            {
                limit--;
                if (limit == 0) yield break;
            }
        }

        await Task.CompletedTask;
    }

    private sealed class LiveSearchResult(FileInfo info) : ISearchResult
    {
        public string Name => info.Name;
        public string FullPath => info.FullName;
        public string ContextDirectory => info.DirectoryName ?? string.Empty;
        public bool IsDir => false;
        public bool IsApplication => false;
        public FileMetadata Metadata => new(info.Length, info.LastWriteTime, info.LastWriteTime, info.LastWriteTime);
    }
}
