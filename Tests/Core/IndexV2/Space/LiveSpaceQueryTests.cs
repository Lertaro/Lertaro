using Lertaro.Core.IndexV2.Space;

namespace Lertaro.Core.Tests.IndexV2.Space;

[TestClass]
public sealed class LiveSpaceQueryTests
{
    [TestMethod]
    public void GetEntries_UsesRecursiveLogicalSizes()
    {
        using var fixture = BuildSample();

        var root = LiveSpaceQuery.GetEntries(fixture.Index, null).Entries.Single();
        var projects = LiveSpaceQuery.GetEntries(fixture.Index, @"C:\Projects").Entries;

        Assert.AreEqual(400, root.Size);
        Assert.AreEqual(300, projects[0].Size);
        Assert.AreEqual(100, projects[1].Size);
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(2000)]
    public void GetEntries_ChecksOnlyListedPathsRegardlessOfSubtreeSize(int fileCount)
    {
        using var fixture = LiveIndexFixture.Build("C", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "Projects", FileRecordFlags.Directory),
        }.Concat(Enumerable.Range(0, fileCount).Select(index =>
            new FileRecord((UInt128)(index + 3), 2, $"file{index}.bin", FileRecordFlags.None, 10))));
        var checkedPaths = new List<string>();
        bool Visible(string path) { checkedPaths.Add(path); return true; }

        var root = LiveSpaceQuery.GetEntries(fixture.Index, null, Visible).Entries.Single();
        Assert.AreEqual(fileCount * 10L, root.Size);
        CollectionAssert.AreEqual(new[] { @"C:\" }, checkedPaths);

        checkedPaths.Clear();
        var entries = LiveSpaceQuery.GetEntries(fixture.Index, @"C:\", Visible).Entries;
        Assert.AreEqual(fileCount * 10L, entries.Single().Size);
        CollectionAssert.AreEqual(new[] { @"C:\", @"C:\Projects" }, checkedPaths);

        var hidden = LiveSpaceQuery.GetEntries(fixture.Index, @"C:\", path => path == @"C:\");
        Assert.IsEmpty(hidden.Entries, "Cached index totals must not cache a caller's permission results.");
    }

    [TestMethod]
    public void GetEntries_ReflectsUncompactedAddDeleteAndMetadataChanges()
    {
        using var fixture = BuildSample();
        fixture.Index.Mutate((_, delta) =>
        {
            delta.Remove(5);
            delta.Upsert(6, 2, "new.bin", FileRecordFlags.None, 700, 0, 0, 0);
            Core.IndexV2.Delta.DeltaLinkOps.UpdateMetadata(delta, 4, 500, 0, 0, 0);
        });

        var root = LiveSpaceQuery.GetEntries(fixture.Index, null).Entries.Single();
        var projects = LiveSpaceQuery.GetEntries(fixture.Index, @"C:\Projects").Entries;

        Assert.AreEqual(1200, root.Size);
        CollectionAssert.AreEqual(new[] { "new.bin", "App" }, projects.Select(entry => entry.Name).ToArray());
        Assert.AreEqual(700, projects[0].Size);
        Assert.AreEqual(500, projects[1].Size);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void GetEntries_DoesNotReopenSnapshotFile(bool checkPermissions)
    {
        using var fixture = BuildSample();
        File.Delete(fixture.Path);
        fixture.Index.Mutate((_, delta) =>
            delta.Upsert(6, 2, "live-only.bin", FileRecordFlags.None, 700, 0, 0, 0));

        var entries = LiveSpaceQuery.GetEntries(fixture.Index, @"C:\Projects", checkPermissions ? _ => true : null).Entries;

        Assert.IsTrue(entries.Any(entry => entry.Name == "live-only.bin" && entry.Size == 700));
    }

    [TestMethod]
    public void GetEntries_CountsHardLinkedFileOnlyOnce()
    {
        using var fixture = LiveIndexFixture.Build("D", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "One", FileRecordFlags.Directory),
            new FileRecord(3, 1, "Two", FileRecordFlags.Directory),
            new FileRecord(4, 2, "shared.bin", FileRecordFlags.None, 512),
            new FileRecord(4, 3, "shared-link.bin", FileRecordFlags.None, 512),
        });

        var root = LiveSpaceQuery.GetEntries(fixture.Index, null).Entries.Single();
        var links = LiveSpaceQuery.GetEntries(fixture.Index, @"D:\One").Entries
            .Concat(LiveSpaceQuery.GetEntries(fixture.Index, @"D:\Two").Entries).ToList();

        Assert.AreEqual(512, root.Size);
        Assert.HasCount(1, links.Where(entry => entry.IsHardLinkDuplicate));
    }

    [TestMethod]
    public void GetEntries_KeepsIndexedHardLinkAccountingWhenPeerIsHidden()
    {
        using var fixture = LiveIndexFixture.Build("D", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "Private", FileRecordFlags.Directory),
            new FileRecord(3, 1, "Public", FileRecordFlags.Directory),
            new FileRecord(4, 2, "shared.bin", FileRecordFlags.None, 512),
            new FileRecord(4, 3, "shared-link.bin", FileRecordFlags.None, 512),
        });
        static bool Visible(string path) => !path.StartsWith(@"D:\Private", StringComparison.OrdinalIgnoreCase);

        var root = LiveSpaceQuery.GetEntries(fixture.Index, null, Visible).Entries.Single();
        var link = LiveSpaceQuery.GetEntries(fixture.Index, @"D:\Public", Visible).Entries.Single();

        Assert.AreEqual(512L, root.Size);
        Assert.AreEqual(0L, link.Size);
        Assert.IsTrue(link.IsHardLinkDuplicate);
    }

    [TestMethod]
    public void GetEntries_ReattributesSizeWhenCanonicalHardLinkIsDeleted()
    {
        using var fixture = LiveIndexFixture.Build("D", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "One", FileRecordFlags.Directory),
            new FileRecord(3, 1, "Two", FileRecordFlags.Directory),
            new FileRecord(4, 2, "shared.bin", FileRecordFlags.None, 512),
            new FileRecord(4, 3, "shared-link.bin", FileRecordFlags.None, 512),
        });
        fixture.Index.Mutate((_, delta) =>
            Core.IndexV2.Delta.DeltaLinkOps.RemoveLink(delta, 4, 2, "shared.bin"));

        var directories = LiveSpaceQuery.GetEntries(fixture.Index, @"D:\").Entries;

        Assert.AreEqual(512, directories.Sum(entry => entry.Size));
        Assert.AreEqual(512, directories.Single(entry => entry.Name == "Two").Size);
    }

    [TestMethod]
    public void GetEntries_ShowsHiddenEntriesButHidesSystemEntries()
    {
        using var fixture = LiveIndexFixture.Build("C", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "visible.txt", FileRecordFlags.None, 100),
            new FileRecord(3, 1, "hidden.txt", FileRecordFlags.Hidden, 200),
            new FileRecord(4, 1, "hidden-folder", FileRecordFlags.Directory | FileRecordFlags.Hidden),
            new FileRecord(5, 1, "system.txt", FileRecordFlags.System, 300),
            new FileRecord(6, 1, "system-folder", FileRecordFlags.Directory | FileRecordFlags.System),
        });

        var children = LiveSpaceQuery.GetEntries(fixture.Index, @"C:\").Entries;

        CollectionAssert.AreEquivalent(
            new[] { "visible.txt", "hidden.txt", "hidden-folder" },
            children.Select(entry => entry.Name).ToArray());
    }

    [TestMethod]
    public async Task GetEntries_PermissionCheckDoesNotBlockIndexUpdatesAndKeepsConsistentSizes()
    {
        using var fixture = BuildSample();
        fixture.Index.Mutate((_, delta) =>
            delta.Upsert(6, 2, "new.bin", FileRecordFlags.None, 700, 0, 0, 0));
        using var checking = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var query = Task.Run(() => LiveSpaceQuery.GetEntries(fixture.Index, null, _ =>
        {
            checking.Set();
            if (!resume.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Permission check was not released.");
            return true;
        }));
        Task? update = null;
        try
        {
            Assert.IsTrue(checking.Wait(TimeSpan.FromSeconds(10)), "The query did not reach its permission check.");
            update = Task.Run(() => fixture.Index.Mutate((_, delta) =>
            {
                Core.IndexV2.Delta.DeltaLinkOps.UpdateMetadata(delta, 6, 900, 0, 0, 0);
                delta.Remove(4);
            }));
            await update.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.AreEqual(1000L, LiveSpaceQuery.GetEntries(fixture.Index, null).Entries.Single().Size);
        }
        finally
        {
            resume.Set();
            if (update != null) await update;
            await query;
        }
        Assert.AreEqual(1100L, (await query).Entries.Single().Size,
            "Permission checks must not mix already-computed totals with later index changes.");
    }

    [TestMethod]
    public void GetEntries_PermissionCheckCanCompactLiveIndex()
    {
        using var fixture = BuildSample();
        fixture.Index.Mutate((_, delta) =>
            delta.Upsert(6, 2, "new.bin", FileRecordFlags.None, 700, 0, 0, 0));
        var changed = false;
        var entries = LiveSpaceQuery.GetEntries(fixture.Index, null, _ =>
        {
            if (!changed)
            {
                changed = true;
                fixture.Index.Compact(fixture.Path);
            }
            return true;
        }).Entries;

        Assert.AreEqual(1100L, entries.Single().Size);
    }

    [TestMethod]
    public void GetEntries_SurvivesServiceDisposingLiveIndexDuringPermissionCheck()
    {
        using var fixture = BuildSample();
        var live = new Core.IndexV2.LiveIndex(Core.IndexV2.Persistence.Snapshot.Open(fixture.Path));
        var disposed = false;
        try
        {
            var entries = LiveSpaceQuery.GetEntries(live, null, _ =>
            {
                if (!disposed)
                {
                    live.Dispose();
                    disposed = true;
                }
                return true;
            }).Entries;

            Assert.AreEqual(400L, entries.Single().Size);
        }
        finally
        {
            if (!disposed) live.Dispose();
        }
    }

    private static LiveIndexFixture BuildSample() => LiveIndexFixture.Build("C", new[]
    {
        LiveIndexFixture.Root(),
        new FileRecord(2, 1, "Projects", FileRecordFlags.Directory),
        new FileRecord(3, 2, "App", FileRecordFlags.Directory),
        new FileRecord(4, 3, "app.exe", FileRecordFlags.None, 300),
        new FileRecord(5, 2, "readme.md", FileRecordFlags.None, 100),
    });
}
