using System.Security.AccessControl;
using System.Security.Principal;
using Lertaro.Core.Indexer.NetworkDrive;
using Lertaro.Core.IndexV2.Delta;
using Lertaro.Core.IndexV2.Persistence;

namespace Lertaro.Core.Tests.IndexV2.Delta;

[TestClass]
public sealed class DeltaPathApplierTests
{
    [TestMethod]
    public void ApplyCreatedOrChanged_ValidDirectory_UpsertsPathAndChildren()
    {
        using var tempDir = new TempDirectory();
        var idxPath = Path.Combine(tempDir.Path, "test.idx");

        var store = new FileRecordStore
        {
            SourceKey = @"\\server\share",
            SourceKind = FileRecordSourceKind.NetworkMappedDrive,
            IdKind = FileRecordIdKind.SourceLocalId64,
            RootId = 1
        };
        store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        SnapshotWriter.Write(store, idxPath);

        using var index = NetworkIndex.FromSnapshotFile(@"\\server\share", idxPath);

        var subDir = Path.Combine(tempDir.Path, "subfolder");
        Directory.CreateDirectory(subDir);
        var subFile = Path.Combine(subDir, "child.txt");
        File.WriteAllText(subFile, "test");

        var changed = index.ApplyCreatedOrChanged(@"\\server\share", subDir);
        Assert.IsTrue(changed);
    }

    [TestMethod]
    public void ApplyDeleted_ExistingPath_RemovesFromIndex()
    {
        using var tempDir = new TempDirectory();
        var idxPath = Path.Combine(tempDir.Path, "test.idx");

        var store = new FileRecordStore
        {
            SourceKey = @"\\server\share",
            SourceKind = FileRecordSourceKind.NetworkMappedDrive,
            IdKind = FileRecordIdKind.SourceLocalId64,
            RootId = 1
        };
        store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        SnapshotWriter.Write(store, idxPath);

        using var index = NetworkIndex.FromSnapshotFile(@"\\server\share", idxPath);

        var subDir = Path.Combine(tempDir.Path, "subfolder");
        Directory.CreateDirectory(subDir);

        index.ApplyCreatedOrChanged(@"\\server\share", subDir);
        var removed = index.ApplyDeleted(subDir);

        Assert.IsTrue(removed);
    }

    [TestMethod]
    public void ApplyCreatedOrChanged_ExistingFile_RefreshesAttributes()
    {
        using var tempDir = new TempDirectory();
        var idxPath = Path.Combine(tempDir.Path, "test.idx");
        var store = new FileRecordStore
        {
            SourceKey = @"\\server\share",
            SourceKind = FileRecordSourceKind.NetworkMappedDrive,
            IdKind = FileRecordIdKind.SourceLocalId64,
            RootId = 1
        };
        store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        SnapshotWriter.Write(store, idxPath);
        using var index = NetworkIndex.FromSnapshotFile(@"\\server\share", idxPath);
        var filePath = Path.Combine(tempDir.Path, "hidden.txt");
        File.WriteAllText(filePath, "test");
        index.ApplyCreatedOrChanged(@"\\server\share", filePath);
        File.SetAttributes(filePath, File.GetAttributes(filePath) | FileAttributes.Hidden);

        index.ApplyCreatedOrChanged(@"\\server\share", filePath);

        Assert.IsTrue(index.ToStore().Records.Single(r => r.Name == "hidden.txt").Flags.HasFlag(FileRecordFlags.Hidden));
    }

    // A directory read can fail after Directory.EnumerateFileSystemEntries has returned: it opens the
    // directory up front but reads it lazily, so a folder that vanishes, a drive that is pulled, or (the
    // deterministic stand-in used here) a "directory" that is really a file throws from MoveNext. A file
    // named "foo " beside a folder "foo" gets there: FileInfo trims the trailing space and reports the
    // folder, so the walk treats the entry as a directory, but the enumeration keeps the space and opens
    // the file. The walk reports it, so a caller that owes a re-walk for it can ask for one.
    [TestMethod]
    public void ApplyCreatedOrChanged_ChildThatFailsWhileBeingRead_SkipsItAndIndexesTheRest()
    {
        using var tempDir = new TempDirectory();
        var drop = Path.Combine(tempDir.Path, "drop");
        Directory.CreateDirectory(Path.Combine(drop, "foo"));
        File.WriteAllText(@"\\?\" + Path.Combine(drop, "foo "), "x");
        File.WriteAllText(Path.Combine(drop, "zzz.txt"), "x");
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var unreadable = new List<string>();

        fixture.Index.Mutate((_, delta) => DeltaPathApplier.ApplyCreatedOrChanged(delta, 1, tempDir.Path, drop, unreadable: unreadable));

        Assert.IsTrue(fixture.Index.ToStore().Records.Any(r => r.Name == "zzz.txt"));
        CollectionAssert.AreEqual(new[] { Path.Combine(drop, "foo ") + Path.DirectorySeparatorChar }, unreadable);
    }

    // A folder we may not list is skipped like before, and not reported: a re-walk cannot read it either,
    // so asking for one would only repeat the walk for every change made around it.
    [TestMethod]
    public void ApplyCreatedOrChanged_ChildWeMayNotList_SkipsItWithoutReportingIt()
    {
        using var tempDir = new TempDirectory();
        var drop = Path.Combine(tempDir.Path, "drop");
        var secret = new DirectoryInfo(Path.Combine(drop, "secret"));
        secret.Create();
        File.WriteAllText(Path.Combine(drop, "zzz.txt"), "x");
        using var identity = WindowsIdentity.GetCurrent();
        var deny = new FileSystemAccessRule(identity.User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var deniedAcl = secret.GetAccessControl();
        deniedAcl.AddAccessRule(deny);
        secret.SetAccessControl(deniedAcl);
        try
        {
            using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
            var unreadable = new List<string>();

            fixture.Index.Mutate((_, delta) => DeltaPathApplier.ApplyCreatedOrChanged(delta, 1, tempDir.Path, drop, unreadable: unreadable));

            var names = fixture.Index.ToStore().Records.Select(r => r.Name).ToList();
            CollectionAssert.IsSubsetOf(new[] { "secret", "zzz.txt" }, names);
            Assert.AreEqual(0, unreadable.Count, string.Join(", ", unreadable));
        }
        finally
        {
            // Setting back an untouched copy of the original ACL persists nothing, so the Deny would stay and
            // the temp directory could never be deleted: remove the rule itself.
            var restored = secret.GetAccessControl();
            restored.RemoveAccessRule(deny);
            secret.SetAccessControl(restored);
        }
    }

    // A stat that fails for a reason other than the entry being gone or denied is reported too. A name no
    // Win32 path may carry is the deterministic stand-in: FileInfo accepts it, and reading its attributes
    // fails with ERROR_INVALID_NAME.
    [TestMethod]
    public void ApplyCreatedOrChanged_EntryWhoseStatFails_ReportsIt()
    {
        using var tempDir = new TempDirectory();
        var path = Path.Combine(tempDir.Path, "a?b");
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var unreadable = new List<string>();
        var changed = true;

        fixture.Index.Mutate((_, delta) => changed = DeltaPathApplier.ApplyCreatedOrChanged(delta, 1, tempDir.Path, path, unreadable: unreadable));

        Assert.IsFalse(changed);
        CollectionAssert.AreEqual(new[] { path }, unreadable);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-delta-test-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
