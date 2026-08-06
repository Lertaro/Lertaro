using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.Wire;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.Core.Tests.Wire;

[TestClass]
public sealed class PipeResponseBinarySerializerTests
{
    private static async Task<PipeResponse> RoundTripAsync(Func<Stream, Task> write)
    {
        using var stream = new MemoryStream();
        await write(stream);
        stream.Position = 0;
        return await PipeResponseBinarySerializer.ReadAsync(stream);
    }

    [TestMethod]
    public async Task RoundTrip_Ok_IsOkAndHasNoMessage()
    {
        var result = await RoundTripAsync(s => PipeResponseBinarySerializer.WriteOkAsync(s));

        Assert.AreEqual(PipeResponseKind.Ok, result.Kind);
        Assert.IsTrue(result.IsOk);
    }

    [TestMethod]
    public async Task RoundTrip_Error_PreservesMessageAndIsNotOk()
    {
        var result = await RoundTripAsync(s => PipeResponseBinarySerializer.WriteErrorAsync(s, "index locked"));

        Assert.AreEqual(PipeResponseKind.Error, result.Kind);
        Assert.AreEqual("index locked", result.Message);
        Assert.IsFalse(result.IsOk);
    }

    [TestMethod]
    public async Task RoundTrip_Status_PreservesScalarFields()
    {
        var status = new UsnIndexer.IndexerStatus
        {
            State = "indexing",
            Progress = 42,
            TotalFiles = 1000,
            TotalDirs = 50,
            ElapsedTime = 3.5,
            IsMaintenanceBusy = true
        };

        var result = await RoundTripAsync(s => PipeResponseBinarySerializer.WriteStatusAsync(s, status));

        Assert.AreEqual("indexing", result.Status!.State);
        Assert.AreEqual(42, result.Status.Progress);
        Assert.AreEqual(1000, result.Status.TotalFiles);
        Assert.AreEqual(50, result.Status.TotalDirs);
        Assert.AreEqual(3.5, result.Status.ElapsedTime);
        Assert.IsTrue(result.Status.IsMaintenanceBusy);
    }

    [TestMethod]
    public async Task RoundTrip_Status_PreservesActiveDrivesAndNestedDriveList()
    {
        var status = new UsnIndexer.IndexerStatus
        {
            State = "ready",
            ActiveDrives = { "C", "D" },
            Drives =
            {
                new UsnIndexer.DriveIndexStatus
                {
                    Drive = "C",
                    Enabled = true,
                    Kind = "LocalNtfs",
                    State = "ready",
                    Files = 5000,
                    Dirs = 200,
                    CachePath = @"c:\cache\c.idx"
                }
            }
        };

        var result = await RoundTripAsync(s => PipeResponseBinarySerializer.WriteStatusAsync(s, status));

        CollectionAssert.AreEqual(new[] { "C", "D" }, result.Status!.ActiveDrives);
        Assert.HasCount(1, result.Status.Drives);
        var drive = result.Status.Drives[0];
        Assert.AreEqual("C", drive.Drive);
        Assert.IsTrue(drive.Enabled);
        Assert.AreEqual("LocalNtfs", drive.Kind);
        Assert.AreEqual(5000, drive.Files);
        Assert.AreEqual(@"c:\cache\c.idx", drive.CachePath);
    }

    [TestMethod]
    public async Task RoundTrip_MachineSettings_PreservesLocalDrives()
    {
        var settings = new MachineSettings { LocalDrives = { "C", "D" } };

        var result = await RoundTripAsync(s => PipeResponseBinarySerializer.WriteMachineSettingsAsync(s, settings));

        CollectionAssert.AreEqual(new[] { "C", "D" }, result.MachineSettings!.LocalDrives);
    }

    [TestMethod]
    public async Task RoundTrip_FileMetadata_PreservesEntries()
    {
        var metadata = new Dictionary<string, FileMetadataEntry>
        {
            [@"c:\readme.txt"] = new FileMetadataEntry(1024, 1_600_000_000, 1_600_000_100, 1_600_000_200)
        };

        var result = await RoundTripAsync(s => PipeResponseBinarySerializer.WriteFileMetadataAsync(s, metadata));

        Assert.HasCount(1, result.FileMetadata!);
        var entry = result.FileMetadata![@"c:\readme.txt"];
        Assert.AreEqual(1024, entry.Size);
        Assert.AreEqual(1_600_000_000u, entry.CreationTimeUnixSeconds);
        Assert.AreEqual(1_600_000_100u, entry.LastWriteTimeUnixSeconds);
        Assert.AreEqual(1_600_000_200u, entry.LastAccessTimeUnixSeconds);
    }

    [TestMethod]
    public async Task RoundTrip_FileMetadata_KeyLookupIsCaseInsensitive()
    {
        var metadata = new Dictionary<string, FileMetadataEntry>
        {
            [@"c:\Readme.txt"] = new FileMetadataEntry(1, 0, 0, 0)
        };

        var result = await RoundTripAsync(s => PipeResponseBinarySerializer.WriteFileMetadataAsync(s, metadata));

        Assert.IsTrue(result.FileMetadata!.ContainsKey(@"c:\README.TXT"));
    }

    [TestMethod]
    public async Task RoundTrip_HookLaunched_PreservesPid()
    {
        var result = await RoundTripAsync(s => PipeResponseBinarySerializer.WriteHookLaunchAsync(s, 4242));

        Assert.AreEqual(PipeResponseKind.HookLaunched, result.Kind);
        Assert.AreEqual(4242, result.Pid);
    }

    [TestMethod]
    public async Task RoundTrip_RecentFiles_PreservesNamePathAndModifiedTime()
    {
        var modifiedUtc = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var recentFiles = new List<SearchResult>
        {
            new()
            {
                Name = "readme.txt",
                Path = @"c:\readme.txt",
                IsDir = false,
                Drive = "C",
                Metadata = new FileMetadata(0, DateTime.MinValue, modifiedUtc.ToLocalTime(), DateTime.MinValue)
            }
        };

        var result = await RoundTripAsync(s => RecentFilesResponseCodec.WriteRecentFilesAsync(s, recentFiles));

        Assert.HasCount(1, result.RecentFiles!);
        var item = result.RecentFiles![0];
        Assert.AreEqual("readme.txt", item.Name);
        Assert.AreEqual(@"c:\readme.txt", item.Path);
        Assert.IsFalse(item.IsDir);
        Assert.AreEqual("C", item.Drive);
        Assert.AreEqual(modifiedUtc, item.Metadata.Modified.ToUniversalTime());
    }

    [TestMethod]
    public async Task ReadAsync_CorruptedMagicHeader_ThrowsInvalidDataException()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => PipeResponseBinarySerializer.ReadAsync(stream));
    }
}
