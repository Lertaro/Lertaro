namespace Lertaro.Plugins.DirectoryOpus.Tests;

// Where dopusrt.exe is allowed to write its answer. Opus accepts only a file it can actually create,
// under a path it can read back, so both the character rules and the writability rule are pinned here.
[TestClass]
public sealed class DopusRtOutputFileTests
{
    // Opus refuses a non-ASCII output path, and a double quote cannot be escaped inside the quoted form
    // the /info argument uses, so both are rejected. A SPACE is not: the path is quoted in the argument,
    // so a temp directory containing one -- a real user name, or a redirected %TEMP% -- is usable and
    // must not be skipped.
    [TestMethod]
    public void IsOpusSafePath_AllowsSpacesAndRejectsNonAsciiAndQuotes()
    {
        Assert.IsTrue(DopusRtOutputFile.IsOpusSafePath(@"C:\Users\testuser\AppData\Local\Temp\lertaro-dopusrt-1a2b.xml"));
        Assert.IsTrue(DopusRtOutputFile.IsOpusSafePath(@"C:\Users\Some Name\AppData\Local\Temp\a.xml"));
        Assert.IsTrue(DopusRtOutputFile.IsOpusSafePath(@"D:\tmp\new test\paths.txt"));
        Assert.IsTrue(DopusRtOutputFile.IsOpusSafePath(@"C:\Users\USER~1\AppData\Local\Temp\lertaro-dopusrt-1a2b.xml"));
        Assert.IsFalse(DopusRtOutputFile.IsOpusSafePath(@"C:\Users\张三\AppData\Local\Temp\a.xml"));
        Assert.IsFalse(DopusRtOutputFile.IsOpusSafePath("C:\\tmp\\new\"test\\a.xml"));
        Assert.IsFalse(DopusRtOutputFile.IsOpusSafePath(null));
        Assert.IsFalse(DopusRtOutputFile.IsOpusSafePath(string.Empty));
    }

    // Every call gets a name nothing else can be holding, so a file left behind by an earlier run can
    // never be read back as this run's folders, and no concurrent query can be writing it.
    [TestMethod]
    public void Create_IsSafeAndFreshPerCall()
    {
        var first = DopusRtOutputFile.Create();
        var second = DopusRtOutputFile.Create();

        if (first == null)
        {
            // No writable ASCII, space-free directory at all: the plugin degrades to the scrape
            // fallback, which the collector's own tests cover.
            Assert.IsNull(second);
            return;
        }

        Assert.IsTrue(DopusRtOutputFile.IsOpusSafePath(first));
        Assert.AreNotEqual(first, second);
        Assert.IsTrue(Directory.Exists(Path.GetDirectoryName(first)));

        // dopusrt only fills in a file that already exists, so the path this returns must already have
        // been created -- empty -- by the time the caller runs the tool.
        Assert.IsTrue(File.Exists(first), $"'{first}' should already exist for dopusrt to fill in");
        Assert.AreEqual(0, new FileInfo(first).Length);

        File.Delete(first);
    }

    // A live log caught the plugin handing dopusrt a path in a directory that passed the character rules
    // but refused the file: dopusrt then exits 0 having written nothing, so EVERY query silently fell
    // through to the scrape (which only sees the focused tab) and paid the full 2s wait. Being ASCII and
    // space-free is therefore not the contract -- being writable is.
    [TestMethod]
    public void Create_OnlyReturnsADirectoryThatCanActuallyHoldTheFile()
    {
        var path = DopusRtOutputFile.Create();
        if (path == null) return; // covered by the test above

        var directory = Path.GetDirectoryName(path)!;

        // The same test the shipped code performs, asserted independently here so the guarantee is
        // pinned by a test rather than only by the production probe.
        var probe = Path.Combine(directory, $"lertaro-test-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(probe, []);
        }
        finally
        {
            try { File.Delete(probe); } catch { /* best effort */ }
        }
    }
}
