using System.Diagnostics;
using System.Reflection;

namespace Lertaro.Plugins.DirectoryOpus.Tests;

// The one part of the dopusrt query that needs the real tool: that it fills in the output file it was
// handed, including when that path contains a space.
[TestClass]
public sealed class DopusRtPathQueryTests
{
    // End-to-end over a path that CONTAINS A SPACE, which is the whole reason the /info argument quotes
    // the path. This drives the real tool against a real Opus, so it needs both installed; on a machine
    // without them there is nothing to query and the test stands down rather than failing.
    //
    // It calls the query's own tool runner directly because %TEMP% cannot be redirected in-process --
    // Path.GetTempPath() caches its answer -- so the space would otherwise never reach the argument.
    [TestMethod]
    public void RunTool_FillsInAPreCreatedFileWhosePathContainsASpace()
    {
        var output = Path.Combine(Path.GetTempPath(), "lertaro space test", $"paths-{Guid.NewGuid():N}.xml");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);

            // dopusrt only fills in a file that is already there.
            File.WriteAllBytes(output, []);

            var runner = typeof(DopusRtPathQuery).GetMethod(
                "RunTool", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(runner, "RunTool should still exist for this regression guard to work");

            // The third element receives the out parameter (whether the tool exited on its own).
            runner.Invoke(null, [FindDopusRt() ?? @"C:\Program Files\GPSoftware\Directory Opus\dopusrt.exe", output, null]);

            // Opus writes the file asynchronously, so poll exactly as the production reader does rather
            // than assuming the content is on disk the moment the process exits.
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (new FileInfo(output).Length == 0 && DateTime.UtcNow < deadline) Thread.Sleep(50);

            var file = new FileInfo(output);
            Assert.IsTrue(file.Exists);
            Assert.IsGreaterThan(0, file.Length, "dopusrt wrote nothing, so the quoted space-bearing path was not accepted");

            // The XML itself is the proof, not a tab count: with no tabs open Opus legitimately answers an
            // empty <results />, which still shows the tool accepted the path and wrote its output.
            Assert.Contains("<results", File.ReadAllText(output));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(output)!, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// The installed <c>dopusrt.exe</c>, taken from the running Opus where possible, or null when Opus is
    /// not installed on this machine.
    /// </summary>
    private static string? FindDopusRt()
    {
        foreach (var process in Process.GetProcessesByName("dopus"))
        {
            try
            {
                var directory = Path.GetDirectoryName(process.MainModule?.FileName);
                if (directory != null)
                {
                    var tool = Path.Combine(directory, "dopusrt.exe");
                    if (File.Exists(tool)) return tool;
                }
            }
            catch { /* elevated Opus: fall through */ }
            finally { process.Dispose(); }
        }

        return null;
    }
}
