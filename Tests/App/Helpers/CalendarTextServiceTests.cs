using Lertaro.App.Helpers;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.Helpers;

/// <summary>
/// Covers the host's side of <see cref="ICalendarTextProvider"/>: which providers it asks, and what it does
/// with the answers.
/// </summary>
[TestClass]
public sealed class CalendarTextServiceTests
{
    private static readonly DateTime Day = new(2026, 10, 4);

    private sealed class Provider(string answer) : ICalendarTextProvider
    {
        public string Name => "test";
        public string Description => "test";
        public string GetCalendarText(DateTime date) => answer;
    }

    private sealed class BrokenProvider : ICalendarTextProvider
    {
        public string Name => "broken";
        public string Description => "broken";
        public string GetCalendarText(DateTime date) => throw new InvalidOperationException("third-party code");
    }

    [TestMethod]
    public void Describe_NoProviderLeft_ReturnsEmpty()
    {
        // What a plugin the user disabled produces: the projection drops it, so the clock line falls back to
        // the host's own date and time with nothing appended.
        Assert.AreEqual(string.Empty, CalendarTextService.Describe([], Day));
    }

    [TestMethod]
    public void Describe_FirstProviderThatAnswersWins()
    {
        // Silence first, so this also pins that an empty answer does not stop the search.
        Assert.AreEqual("八月廿四",
            CalendarTextService.Describe([new Provider(string.Empty), new Provider("   "), new Provider("八月廿四")], Day));
    }

    [TestMethod]
    public void Describe_BrokenProvider_DoesNotTakeTheLineDown()
    {
        Assert.AreEqual("八月廿四",
            CalendarTextService.Describe([new BrokenProvider(), new Provider("八月廿四")], Day));
    }

    [TestMethod]
    public void Describe_BrokenProviderAlone_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, CalendarTextService.Describe([new BrokenProvider()], Day));
    }
}
