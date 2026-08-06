using Lertaro.App.Services.Update;

namespace Lertaro.App.Tests.Services.Update;

[TestClass]
public sealed class UpdateCheckServiceTests
{
    [TestMethod]
    public void IsNewerVersion_TagGreaterThanCurrent_ReturnsTrue()
    {
        var isNewer = UpdateCheckService.IsNewerVersion("v2.0.0", new Version(1, 0, 0), out var parsed);

        Assert.IsTrue(isNewer);
        Assert.AreEqual(new Version(2, 0, 0), parsed);
    }

    [TestMethod]
    public void IsNewerVersion_TagEqualToCurrent_ReturnsFalse() =>
        Assert.IsFalse(UpdateCheckService.IsNewerVersion("v1.0.0", new Version(1, 0, 0), out _));

    [TestMethod]
    public void IsNewerVersion_TagOlderThanCurrent_ReturnsFalse() =>
        Assert.IsFalse(UpdateCheckService.IsNewerVersion("v0.9.0", new Version(1, 0, 0), out _));

    [TestMethod]
    public void IsNewerVersion_UppercaseVPrefix_IsStripped() =>
        Assert.IsTrue(UpdateCheckService.IsNewerVersion("V2.0.0", new Version(1, 0, 0), out _));

    [TestMethod]
    public void IsNewerVersion_NoVPrefix_StillParses() =>
        Assert.IsTrue(UpdateCheckService.IsNewerVersion("2.0.0", new Version(1, 0, 0), out _));

    [TestMethod]
    public void IsNewerVersion_UnparseableTag_ReturnsFalse() =>
        Assert.IsFalse(UpdateCheckService.IsNewerVersion("not-a-version", new Version(1, 0, 0), out _));

    [TestMethod]
    public void IsNewerVersion_UnparseableTag_LatestVersionOutIsNull()
    {
        UpdateCheckService.IsNewerVersion("not-a-version", new Version(1, 0, 0), out var parsed);

        Assert.IsNull(parsed);
    }

    [TestMethod]
    public void IsNewerVersion_NullCurrentVersion_TreatsAnyParsedVersionAsNewer() =>
        Assert.IsTrue(UpdateCheckService.IsNewerVersion("v1.0.0", null, out _));

    [TestMethod]
    public void IsNewerVersion_PatchVersionDifferenceOnly_IsDetected() =>
        Assert.IsTrue(UpdateCheckService.IsNewerVersion("v1.0.1", new Version(1, 0, 0), out _));

    [TestMethod]
    public void IsNewerVersion_MajorVersionOlderButMinorHigher_StillOlderOverall() =>
        // Version comparison is lexicographic by component (major, then minor, ...), not a single number.
        Assert.IsFalse(UpdateCheckService.IsNewerVersion("v1.99.0", new Version(2, 0, 0), out _));
}
