using Lertaro.Core.Wire;

namespace Lertaro.Core.Tests.Wire;

// The watch-and-notify pair, both directions. A payload whose size, write and read halves drift apart
// does not fail loudly -- it arrives empty, or corrupts whatever the reader parses next out of the same
// buffer, and the feature simply never fires. Both messages here carry a variable-length string list,
// which is the shape that goes wrong that way.
[TestClass]
public sealed class DirectoryChangeSubscriptionWireTests
{
    [TestMethod]
    public void TheWatchListSurvivesTheTrip()
    {
        var sent = new SearchRequestMessage
        {
            Id = SearchRequestId.SubscribeDirectoryChanges,
            Directories = new List<string>
            {
                @"C:\ProgramData\Microsoft\Windows\Start Menu",
                @"C:\Users\me\Desktop",
                @"\\nas\media\music",
            },
        };

        var received = RoundTrip(sent);

        Assert.AreEqual(SearchRequestId.SubscribeDirectoryChanges, received.Id);
        CollectionAssert.AreEqual(sent.Directories, received.Directories);
    }

    // Subscribing to nothing is what a client with no registrations sends, and it must not be mistaken
    // for a subscription that never arrived.
    [TestMethod]
    public void AnEmptyWatchListIsStillAWatchList()
    {
        var received = RoundTrip(new SearchRequestMessage
        {
            Id = SearchRequestId.SubscribeDirectoryChanges,
            Directories = new List<string>(),
        });

        Assert.AreEqual(SearchRequestId.SubscribeDirectoryChanges, received.Id);
        Assert.IsNotNull(received.Directories);
        Assert.IsEmpty(received.Directories);
    }

    [TestMethod]
    public async Task TheNotificationSurvivesTheTrip()
    {
        var hits = new List<string> { @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs", @"C:\Users\me\Desktop" };

        var received = await RoundTripAsync(new PipeResponse
        {
            Kind = PipeResponseKind.DirectoriesChanged,
            ChangedDirectories = hits,
        });

        Assert.AreEqual(PipeResponseKind.DirectoriesChanged, received.Kind);
        CollectionAssert.AreEqual(hits, received.ChangedDirectories);
    }

    // Non-ASCII paths are the ordinary case here -- a Start Menu folder is localised on most machines --
    // and a byte count taken in characters rather than UTF-8 bytes truncates exactly these.
    [TestMethod]
    public async Task ANonAsciiPathIsNotTruncated()
    {
        var hits = new List<string> { @"C:\用户\桌面\我的文件夹", @"C:\Program Files\日本語" };

        var received = await RoundTripAsync(new PipeResponse
        {
            Kind = PipeResponseKind.DirectoriesChanged,
            ChangedDirectories = hits,
        });

        CollectionAssert.AreEqual(hits, received.ChangedDirectories);
    }

    [TestMethod]
    public async Task NoHitsIsAWellFormedMessage()
    {
        var received = await RoundTripAsync(new PipeResponse
        {
            Kind = PipeResponseKind.DirectoriesChanged,
            ChangedDirectories = new List<string>(),
        });

        Assert.AreEqual(PipeResponseKind.DirectoriesChanged, received.Kind);
        Assert.IsEmpty(received.ChangedDirectories!);
    }

    private static SearchRequestMessage RoundTrip(SearchRequestMessage message)
    {
        using var stream = new MemoryStream();
        SearchRequestBinarySerializer.WriteSearchRequestAsync(stream, message, CancellationToken.None).GetAwaiter().GetResult();
        stream.Position = 0;
        return SearchRequestBinarySerializer.ReadSearchRequestAsync(stream, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static async Task<PipeResponse> RoundTripAsync(PipeResponse response)
    {
        using var stream = new MemoryStream();
        await PipeResponseBinarySerializer.WriteAsync(stream, response, CancellationToken.None);
        stream.Position = 0;
        return await PipeResponseBinarySerializer.ReadAsync(stream, CancellationToken.None);
    }
}
