namespace Lertaro.Plugins.BrowserData.Tests;

[TestClass]
public sealed class BrowserHistoryTimeTests
{
    [TestMethod]
    public void FromChromium_Zero_IsChromiumEpoch()
    {
        var timestamp = BrowserHistoryTime.FromChromium(0);

        Assert.AreEqual(new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero), timestamp);
    }

    [TestMethod]
    public void FromFirefox_OneMillion_IsOneSecondAfterUnixEpoch()
    {
        var timestamp = BrowserHistoryTime.FromFirefox(1_000_000);

        Assert.AreEqual(DateTimeOffset.UnixEpoch.AddSeconds(1), timestamp);
    }

    [TestMethod]
    public void FromChromium_OutOfRangeTimestamp_ReturnsNull() => Assert.IsNull(BrowserHistoryTime.FromChromium(long.MaxValue));

    [TestMethod]
    public void FromFirefox_OutOfRangeTimestamp_ReturnsNull() => Assert.IsNull(BrowserHistoryTime.FromFirefox(long.MaxValue));

    [TestMethod]
    public void Format_UsesYearMonthDayAndMinute()
    {
        var timestamp = new DateTimeOffset(2026, 9, 2, 20, 34, 0, TimeSpan.Zero);

        StringAssert.Matches(BrowserHistoryTime.Format(timestamp), new System.Text.RegularExpressions.Regex(@"^\d{4}/\d{2}/\d{2} \d{2}:\d{2}$"));
    }
}
