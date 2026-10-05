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
        try
        {
            if (first == null || second == null)
                Assert.Inconclusive("Requires a writable ASCII output directory; Directory Opus itself is not required.");

            Assert.AreNotEqual(first, second);
            foreach (var path in new[] { first, second })
            {
                Assert.IsTrue(DopusRtOutputFile.IsOpusSafePath(path));
                Assert.IsTrue(File.Exists(path), "dopusrt requires a pre-created output file");
                Assert.AreEqual(0L, new FileInfo(path).Length);
                File.WriteAllText(path, "<results />");
                Assert.AreEqual("<results />", File.ReadAllText(path));
            }
        }
        finally
        {
            if (first != null) File.Delete(first);
            if (second != null) File.Delete(second);
        }
    }
}
