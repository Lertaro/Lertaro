using Lertaro.Core.Wire;

namespace Lertaro.Core.Tests.Wire;

[TestClass]
public sealed class SearchRequestBinarySerializerTests
{
    private static async Task<SearchRequestMessage> RoundTripAsync(SearchRequestMessage message)
    {
        using var stream = new MemoryStream();
        await SearchRequestBinarySerializer.WriteSearchRequestAsync(stream, message);
        stream.Position = 0;
        return await SearchRequestBinarySerializer.ReadSearchRequestAsync(stream);
    }

    [TestMethod]
    public async Task RoundTrip_Search_PreservesExactMatchFlag()
    {
        foreach (var id in new[] { SearchRequestId.Search, SearchRequestId.SearchDir })
        {
            var result = await RoundTripAsync(new SearchRequestMessage
            {
                Id = id,
                Query = "report",
                DirectoryFilter = @"C:\docs",
                Limit = 51,
                AppLimit = 51,
                ExactMatch = true
            });

            Assert.IsTrue(result.ExactMatch, $"{id} lost the flag");
            // The flag is written after the alias list, so a wrong payload size would corrupt
            // whatever precedes it rather than only the flag itself.
            Assert.AreEqual("report", result.Query);
            Assert.AreEqual(51, result.Limit);
        }
    }

    [TestMethod]
    public async Task RoundTrip_Search_DefaultsToFuzzyWhenFlagNeverSet()
    {
        // SearchRequestMessage is a struct and cannot carry a field initializer, so the wire flag is
        // phrased as the negative: a caller that never touches it must still get fuzzy matching.
        var result = await RoundTripAsync(new SearchRequestMessage { Id = SearchRequestId.Search, Query = "report" });

        Assert.IsFalse(result.ExactMatch);
    }

    [TestMethod]
    public async Task RoundTrip_EnumerateDir_PreservesDirectoryFilterPatternAndRecursion()
    {
        var result = await RoundTripAsync(new SearchRequestMessage
        {
            Id = SearchRequestId.EnumerateDir,
            DirectoryFilter = @"C:\Program Files\某个目录",
            Query = "*.exe;*.lnk",
            Recursive = true,
            Limit = 0
        });

        Assert.AreEqual(SearchRequestId.EnumerateDir, result.Id);
        Assert.AreEqual(@"C:\Program Files\某个目录", result.DirectoryFilter);
        Assert.AreEqual("*.exe;*.lnk", result.Query);
        Assert.IsTrue(result.Recursive);
        Assert.AreEqual(0, result.Limit);
    }

    // Same struct-default reasoning as ExactMatch: the flag is written last, so a caller that never
    // sets it must come out as the cheap single-level listing rather than a whole subtree walk.
    [TestMethod]
    public async Task RoundTrip_EnumerateDir_DefaultsToNonRecursive()
    {
        var result = await RoundTripAsync(new SearchRequestMessage
        {
            Id = SearchRequestId.EnumerateDir,
            DirectoryFilter = @"C:\docs",
            Query = "*",
            Limit = 25
        });

        Assert.IsFalse(result.Recursive);
        Assert.AreEqual(25, result.Limit);
        Assert.AreEqual(@"C:\docs", result.DirectoryFilter);
    }

    [TestMethod]
    public async Task RoundTrip_NoPayloadRequest_PreservesId()
    {
        var result = await RoundTripAsync(new SearchRequestMessage { Id = SearchRequestId.Ping });

        Assert.AreEqual(SearchRequestId.Ping, result.Id);
    }

    [TestMethod]
    public async Task RoundTrip_SetMachineSettings_PreservesLocalDrives()
    {
        var settings = new MachineSettings { LocalDrives = { "C", "D", "Z" } };
        var result = await RoundTripAsync(new SearchRequestMessage
        {
            Id = SearchRequestId.SetMachineSettings,
            MachineSettings = settings
        });

        CollectionAssert.AreEqual(new[] { "C", "D", "Z" }, result.MachineSettings!.LocalDrives);
    }

    [TestMethod]
    public async Task RoundTrip_SetMachineSettings_EmptyDriveList()
    {
        var result = await RoundTripAsync(new SearchRequestMessage
        {
            Id = SearchRequestId.SetMachineSettings,
            MachineSettings = new MachineSettings()
        });

        Assert.IsEmpty(result.MachineSettings!.LocalDrives);
    }

    [TestMethod]
    public async Task RoundTrip_RebuildDrive_PreservesDriveString()
    {
        var result = await RoundTripAsync(new SearchRequestMessage
        {
            Id = SearchRequestId.RebuildDrive,
            Drive = "C"
        });

        Assert.AreEqual("C", result.Drive);
    }

    [TestMethod]
    public async Task RoundTrip_Search_PreservesLimitsQueryAndDisabledAliasComponents()
    {
        var result = await RoundTripAsync(new SearchRequestMessage
        {
            Id = SearchRequestId.Search,
            Limit = 50,
            AppLimit = 10,
            Query = "readme",
            DisabledAliasComponents = new List<string> { "pinyin", "ime" }
        });

        Assert.AreEqual(50, result.Limit);
        Assert.AreEqual(10, result.AppLimit);
        Assert.AreEqual("readme", result.Query);
        CollectionAssert.AreEqual(new[] { "pinyin", "ime" }, result.DisabledAliasComponents);
    }

    [TestMethod]
    public async Task RoundTrip_Search_UnicodeQuery()
    {
        var result = await RoundTripAsync(new SearchRequestMessage
        {
            Id = SearchRequestId.Search,
            Query = "文件搜索"
        });

        Assert.AreEqual("文件搜索", result.Query);
    }

    [TestMethod]
    public async Task RoundTrip_SearchDir_PreservesDirectoryFilterAndQuery()
    {
        var result = await RoundTripAsync(new SearchRequestMessage
        {
            Id = SearchRequestId.SearchDir,
            Limit = 20,
            AppLimit = 5,
            DirectoryFilter = @"c:\projects",
            Query = "notes"
        });

        Assert.AreEqual(@"c:\projects", result.DirectoryFilter);
        Assert.AreEqual("notes", result.Query);
        Assert.AreEqual(20, result.Limit);
    }

    [TestMethod]
    public async Task RoundTrip_GetFileMetadata_PreservesFilePaths()
    {
        var result = await RoundTripAsync(new SearchRequestMessage
        {
            Id = SearchRequestId.GetFileMetadata,
            FilePaths = new List<string> { @"c:\a.txt", @"c:\b.txt" }
        });

        CollectionAssert.AreEqual(new[] { @"c:\a.txt", @"c:\b.txt" }, result.FilePaths);
    }

    [TestMethod]
    public async Task RoundTrip_GetRecentFiles_PreservesDirectoriesLimitAndMaxAge()
    {
        var result = await RoundTripAsync(new SearchRequestMessage
        {
            Id = SearchRequestId.GetRecentFiles,
            Limit = 30,
            MaxAgeMinutes = 1440,
            Directories = new List<string> { @"c:\Downloads" }
        });

        Assert.AreEqual(30, result.Limit);
        Assert.AreEqual(1440, result.MaxAgeMinutes);
        CollectionAssert.AreEqual(new[] { @"c:\Downloads" }, result.Directories);
    }

    [TestMethod]
    public async Task RoundTrip_GetSpaceEntries_PreservesDirectory()
    {
        var result = await RoundTripAsync(new SearchRequestMessage
        {
            Id = SearchRequestId.GetSpaceEntries,
            Drive = @"C:\Projects"
        });

        Assert.AreEqual(SearchRequestId.GetSpaceEntries, result.Id);
        Assert.AreEqual(@"C:\Projects", result.Drive);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task RoundTrip_LaunchHook_PreservesRequestElevation(bool requestElevation)
    {
        var result = await RoundTripAsync(new SearchRequestMessage
        {
            Id = SearchRequestId.LaunchHook,
            RequestElevation = requestElevation
        });

        Assert.AreEqual(requestElevation, result.RequestElevation);
    }

    [TestMethod]
    public async Task ReadSearchRequestAsync_WrongVersion_ThrowsInvalidDataException()
    {
        using var stream = new MemoryStream();
        // Write a frame using the sibling PipeRequestBinarySerializer's own (different) version tag,
        // sharing the same magic number, to simulate a version mismatch on the wire.
        await PipeRequestBinarySerializer.WriteStringAsync(stream, "not a search request");
        stream.Position = 0;

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => SearchRequestBinarySerializer.ReadSearchRequestAsync(stream));
    }
}
