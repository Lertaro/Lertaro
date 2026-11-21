using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Lertaro.PluginSdk;

namespace Lertaro.Plugins.DirectoryOpus;

/// <summary>
/// Selects and validates the output file that dopusrt fills with query results.
/// </summary>
internal static class DopusRtOutputFile
{
    /// <summary>
    /// Creates a unique empty output file for one dopusrt query, or returns null if no usable location
    /// exists. Opus requires an existing, unlocked file with an ASCII path containing no double quote;
    /// spaces are allowed. Try an 8.3 path for non-ASCII temporary directories.
    ///
    /// Probe create/delete access before accepting a directory: syntactically valid paths may still be
    /// blocked by ACLs or security software, and dopusrt can exit successfully without writing a result.
    /// </summary>
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
    /// Returns candidate output directories in preference order: the temporary directory (8.3 form first),
    /// a temporary subdirectory, then local App data. Resolve final paths and deduplicate after trimming
    /// trailing separators. A subdirectory may be writable when the temp root is blocked. Build eagerly so
    /// all candidate directories are prepared.
    /// </summary>
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
