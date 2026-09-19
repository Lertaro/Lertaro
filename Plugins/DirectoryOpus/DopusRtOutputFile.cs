using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Lertaro.PluginSdk;

namespace Lertaro.Plugins.DirectoryOpus;

/// <summary>
/// The file <c>dopusrt.exe</c> fills in with its answer: where it is allowed to live, and whether a path
/// is one Opus will accept as that file.
/// </summary>
/// <remarks>
/// Split out of <see cref="DopusRtPathQuery"/> purely to keep that file under the repo's per-file line
/// limit; this class has no state of its own, it always operates on the one output file a query needs.
/// </remarks>
internal static class DopusRtOutputFile
{
    /// <summary>
    /// A fresh output path for one query, already created empty, or null when this machine has none Opus
    /// can use.
    /// </summary>
    /// <remarks>
    /// Opus requires the file to be one it can actually write: an ASCII path with no double quote in it
    /// (the argument quotes the path, so a quote could not be escaped), a file that already exists, and a
    /// file nothing else holds. Hence: a NEW name every call (never a reused one a previous run could have
    /// left behind or a concurrent query could be writing), created empty before dopusrt is asked to fill
    /// it in, in a directory validated against those rules. A SPACE is allowed -- the path is quoted in the
    /// argument -- which matters because %TEMP% is per-user and routinely contains one; its 8.3 short form
    /// is the ASCII-only spelling of the same existing directory, which is what covers a non-ASCII user
    /// name.
    ///
    /// Passing those character rules is NOT enough, which is what a live log caught: %TEMP% can be
    /// spelled perfectly and still refuse the file, and dopusrt then exits 0 having written nothing, so
    /// every query silently fell through to the window scrape -- which can only see the tabs whose
    /// container is visible, i.e. the focused one -- and paid the whole 2s wait for the privilege. A
    /// directory is therefore used only after a real file has been created in it and removed again, so a
    /// restricted ACL, a security product or a redirected/sandboxed %TEMP% is rejected up front instead
    /// of being discovered once per query.
    /// </remarks>
    internal static string? Create()
    {
        foreach (var directory in CandidateDirectories())
        {
            var candidate = Path.Combine(directory, $"lertaro-dopusrt-{Guid.NewGuid():N}.xml");
            if (!IsOpusSafePath(candidate) || !IsWritable(directory)) continue;

            // dopusrt only fills in a file that is already there, so the empty file is created here and
            // the plugin never invents a name: a name of our own that nothing has created yet is one
            // dopusrt will not use. This also keeps the file handle closed, which it must be -- dopusrt
            // cannot write a file another process is holding open.
            File.WriteAllBytes(candidate, []);

            Logger.Log($"[DirectoryOpus] dopusrt output directory: '{directory}'.", LogLevel.Debug);
            return candidate;
        }

        Logger.Log("[DirectoryOpus] no writable, ASCII, quote-free directory for the dopusrt query; using the window scrape.", LogLevel.Debug);
        return null;
    }

    /// <summary>
    /// The directories to consider, most preferred first: %TEMP% (short form first, since the long form
    /// may be non-ASCII), a subdirectory of it, then our own directory under the local app data.
    /// </summary>
    /// <remarks>
    /// Every entry is a full path that already exists or can be created here -- dopusrt does not expand
    /// environment variables or 8.3 aliases for us, so what it is handed has to be the final spelling.
    /// The %TEMP% entries are deduplicated because when a directory's own name is already 8.3-compatible
    /// its short form IS its long form; the trailing separator is dropped before comparing so that holds
    /// for the long form too, which <see cref="Path.GetTempPath"/> always spells with one.
    /// The subdirectory matters: a sandbox or a security product can deny writes to the %TEMP% root while
    /// allowing them one level down, which is exactly the shape of the failure that was measured, and it
    /// keeps the file on the temp volume instead of putting it beside the user's real app data.
    /// Built eagerly rather than yielded: these are alternatives to be tried in order, and a lazy list
    /// would not even create the later ones once an earlier candidate was accepted.
    /// </remarks>
    private static IReadOnlyList<string> CandidateDirectories()
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var temp = Path.TrimEndingDirectorySeparator(Path.GetTempPath());
        var shortTemp = ToShortForm(temp);

        foreach (var directory in new[] { shortTemp, temp })
        {
            if (string.IsNullOrEmpty(directory) || !seen.Add(directory)) continue;
            candidates.Add(directory);
        }

        // A directory of our own, first under %TEMP% and then under the app's data: created on first use,
        // so a machine whose %TEMP% root is locked down still has somewhere to answer from.
        foreach (var root in new[] { shortTemp ?? temp, AppDataRoot() })
        {
            var own = CreateOutputDirectory(root);
            if (own != null && seen.Add(own)) candidates.Add(own);
        }

        return candidates;
    }

    private static string? AppDataRoot() =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) is { Length: > 0 } root
            ? Path.Combine(root, "Lertaro")
            : null;

    private static string? CreateOutputDirectory(string? root)
    {
        try
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return null;

            var directory = Path.Combine(root, "LertaroTemp");
            Directory.CreateDirectory(directory);
            return directory;
        }
        catch (Exception)
        {
            // Creating it is allowed to fail; that just means this candidate is not available.
            return null;
        }
    }

    /// <summary>
    /// Whether a file can really be created in <paramref name="directory"/>. The only honest test is to
    /// create one -- an ACL, a security product or a sandbox is invisible to any pre-flight check -- so
    /// this writes and immediately removes a uniquely named file.
    /// </summary>
    private static bool IsWritable(string directory)
    {
        var probe = Path.Combine(directory, $"lertaro-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(probe, []);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            try { File.Delete(probe); } catch { /* never created, or best effort */ }
        }
    }

    /// <summary>
    /// Whether a path can be handed to Opus in the <c>/info</c> argument: ASCII only, since Opus refuses
    /// a non-ASCII output path, and with no double quote, which cannot be escaped inside the quoted form
    /// the argument uses. Spaces are explicitly allowed -- the path is quoted, so a temp directory with a
    /// space in it (a real user name, or a redirected %TEMP%) is no longer a reason to skip the query.
    /// Pure, so the rule is pinned by a test rather than by this machine's temp directory happening to be
    /// a friendly one.
    /// </summary>
    internal static bool IsOpusSafePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        foreach (var character in path)
        {
            if (character < 0x20 || character > 0x7E || character == '"') return false;
        }

        return true;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(string longPath, StringBuilder shortPath, uint capacity);

    /// <summary>The 8.3 spelling of a directory (<c>C:\Users\ZHANG~1\...</c>), which is ASCII by construction.</summary>
    private static string? ToShortForm(string path)
    {
        try
        {
            var buffer = new StringBuilder(520);
            var length = GetShortPathNameW(path, buffer, (uint)buffer.Capacity);
            return length > 0 && length < buffer.Capacity ? buffer.ToString().TrimEnd('\\') : null;
        }
        catch { return null; }
    }
}
