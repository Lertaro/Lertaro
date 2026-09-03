using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Walk;

[TestClass]
public sealed class NativeFileEnumeratorTests
{
    [TestMethod]
    public void Enumerate_TempDirectoryWithFilesAndSubdirectories_YieldsNamesAndAttributes()
    {
        var dir = Directory.CreateTempSubdirectory("lertaro-tests-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "a.txt"), "x");
            var subDir = Directory.CreateDirectory(Path.Combine(dir.FullName, "sub"));
            File.WriteAllText(Path.Combine(subDir.FullName, "b.txt"), "y");

            var entries = NativeFileEnumerator.Enumerate(dir.FullName).ToList();

            CollectionAssert.AreEquivalent(new[] { "a.txt", "sub" }, entries.Select(e => e.Name).ToList());
            Assert.IsTrue(entries.All(e => e.Name is not ("." or "..")));

            var fileEntry = entries.Single(e => e.Name == "a.txt");
            Assert.IsFalse(fileEntry.IsDirectory);
            Assert.IsFalse(fileEntry.Attributes.HasFlag(FileAttributes.Directory));

            var directoryEntry = entries.Single(e => e.Name == "sub");
            Assert.IsTrue(directoryEntry.IsDirectory);
            Assert.IsTrue(directoryEntry.Attributes.HasFlag(FileAttributes.Directory));
        }
        finally
        {
            try { Directory.Delete(dir.FullName, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void Enumerate_EmptyDirectory_YieldsNoEntries()
    {
        var dir = Directory.CreateTempSubdirectory("lertaro-tests-");
        try
        {
            var entries = NativeFileEnumerator.Enumerate(dir.FullName).ToList();

            Assert.IsEmpty(entries);
        }
        finally
        {
            try { Directory.Delete(dir.FullName, recursive: true); } catch { }
        }
    }
}
