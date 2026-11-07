namespace Lertaro.Core.Tests.Settings;

// The prefix the user types and the prefix the updater concatenates are two different strings, and the
// only thing standing between them is Normalize. A prefix that keeps zero or two trailing slashes produces
// "https://mirror/https://github.com/..." with a doubled or missing separator, which some proxies answer
// with a 404 and others with somebody else's file -- so the slash rule is pinned here rather than left to
// whichever call site remembers it.
[TestClass]
public sealed class UpdateMirrorSettingsTests
{
    [TestMethod]
    public void Normalize_NullOrEmpty_ReturnsNoMirrors()
    {
        Assert.IsEmpty(UpdateMirrorSettings.Normalize(null));
        Assert.IsEmpty(UpdateMirrorSettings.Normalize([]));
    }

    [TestMethod]
    [DataRow("https://mirror.example.com")]
    [DataRow("https://mirror.example.com/")]
    [DataRow("https://mirror.example.com///")]
    [DataRow("  https://mirror.example.com/  ")]
    public void Normalize_LeavesExactlyOneTrailingSlashWhateverWasTyped(string typed)
        => CollectionAssert.AreEqual(
            new[] { "https://mirror.example.com/" },
            UpdateMirrorSettings.Normalize([typed]));

    [TestMethod]
    public void Normalize_KeepsAPathAndHttpScheme()
        => CollectionAssert.AreEqual(
            new[] { "http://mirror.example.com:8080/github/" },
            UpdateMirrorSettings.Normalize(["http://mirror.example.com:8080/github"]));

    [TestMethod]
    public void Normalize_KeepsAtMostThreeInOrder()
        => CollectionAssert.AreEqual(
            new[] { "https://one.example.com/", "https://two.example.com/", "https://three.example.com/" },
            UpdateMirrorSettings.Normalize(
            [
                "https://one.example.com/", "https://two.example.com/", "https://three.example.com/",
                "https://four.example.com/", "https://five.example.com/",
            ]));

    [TestMethod]
    public void Normalize_DropsDuplicatesCaseInsensitivelyIncludingSlashVariants()
        => CollectionAssert.AreEqual(
            new[] { "https://Mirror.Example.com/", "https://other.example.com/" },
            UpdateMirrorSettings.Normalize(
            [
                "https://Mirror.Example.com", "https://mirror.example.com/", "https://MIRROR.EXAMPLE.COM///",
                "https://other.example.com/",
            ]));

    [TestMethod]
    public void Normalize_CompactsSoABlankSlotLeavesNoHoleAheadOfAFilledOne()
        => CollectionAssert.AreEqual(
            new[] { "https://second.example.com/", "https://third.example.com/" },
            UpdateMirrorSettings.Normalize(["", "https://second.example.com/", "   ", "https://third.example.com/"]));

    [TestMethod]
    public void Normalize_DropsEntriesAheadOfTheCapSoTheThirdSlotIsNotWastedOnAnInvalidOne()
        => CollectionAssert.AreEqual(
            new[] { "https://one.example.com/", "https://two.example.com/", "https://three.example.com/" },
            UpdateMirrorSettings.Normalize(
            [
                "ftp://bad.example.com/", "https://one.example.com/", "https://", "https://two.example.com/",
                "not-a-url", "https://three.example.com/", "https://four.example.com/",
            ]));

    [TestMethod]
    [DataRow(null, false)]
    [DataRow("", false)]
    [DataRow("   ", false)]
    [DataRow("https://mirror.example.com/", true)]
    [DataRow("http://mirror.example.com", true)]
    [DataRow("https://mirror.example.com/path", true)]
    [DataRow("  https://mirror.example.com/  ", true)]
    // Uri accepts these as absolute while naming nothing to connect to, or naming a scheme HttpClient
    // must never be handed a user-supplied prefix for.
    [DataRow("https://", false)]
    [DataRow("http://", false)]
    [DataRow("ftp://mirror.example.com/", false)]
    [DataRow("file:///C:/mirror", false)]
    [DataRow("javascript:alert(1)", false)]
    [DataRow("mirror.example.com", false)]
    [DataRow("/relative/path", false)]
    public void IsValid_AcceptsOnlyAnAbsoluteHttpUrlNamingAHost(string? mirror, bool expected)
        => Assert.AreEqual(expected, UpdateMirrorSettings.IsValid(mirror));
}