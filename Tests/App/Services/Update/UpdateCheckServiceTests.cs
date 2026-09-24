using Lertaro.App.Services.Update;

using Lertaro.Core;

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

    private static readonly DateTimeOffset FailureTime = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private static UserSettings SettingsThatJustFailedOn(string tag) => new()
    {
        LastFailedUpdateTag = tag,
        LastFailedUpdateUtcTicks = FailureTime.UtcTicks
    };

    [TestMethod]
    public void IsInCooldown_SameReleaseWithinWindow_IsCooledDown() =>
        Assert.IsTrue(UpdateCheckService.IsInCooldown(
            SettingsThatJustFailedOn("v9.0.0"), "v9.0.0", FailureTime + TimeSpan.FromHours(23)));

    [TestMethod]
    public void IsInCooldown_SameReleaseAfterWindow_TriesAgain() =>
        // Half-open on purpose: the retry is due once the window has passed, not one clock tick later.
        Assert.IsFalse(UpdateCheckService.IsInCooldown(
            SettingsThatJustFailedOn("v9.0.0"), "v9.0.0", FailureTime + UpdateCheckService.UpdateRetryCooldown));

    [TestMethod]
    public void IsInCooldown_DifferentRelease_IsNotCooledDown() =>
        // Only the release that failed is left alone, so a fix published an hour later still arrives.
        Assert.IsFalse(UpdateCheckService.IsInCooldown(
            SettingsThatJustFailedOn("v9.0.0"), "v9.0.1", FailureTime + TimeSpan.FromMinutes(1)));

    [TestMethod]
    public void IsInCooldown_NeverAttempted_IsNotCooledDown() =>
        Assert.IsFalse(UpdateCheckService.IsInCooldown(
            new UserSettings { LastFailedUpdateTag = "v9.0.0" }, "v9.0.0", FailureTime));

    [TestMethod]
    public void IsInCooldown_FailureTimestampFromTheFuture_IsNotCooledDownForever() =>
        // A clock that moves backwards (NTP correction, a wrong BIOS) must not park updates indefinitely,
        // and must not throw either -- this reads a value another process wrote.
        Assert.IsFalse(UpdateCheckService.IsInCooldown(
            SettingsThatJustFailedOn("v9.0.0"), "v9.0.0", FailureTime - TimeSpan.FromDays(365)));
}
