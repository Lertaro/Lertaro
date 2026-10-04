using System.IO;
using Lertaro.App.Helpers;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.Helpers;

/// <summary>
/// Covers the host's side of <see cref="ICalendarTextProvider"/>: which providers it asks, and what it does
/// with the answers.
/// </summary>
/// <remarks>
/// The rule that a disabled plugin is never asked lives in <c>PluginManager.CalendarTextProviders</c>, which
/// is the enabled-only projection, so it is pinned in two ways here: the behavioural cases run against a
/// caller-supplied provider list (an empty one is exactly what a disabled plugin leaves behind), and the
/// last test reads the source to prove the production overload is handed that projection rather than the
/// unfiltered <c>AllCalendarTextProviders</c>.
/// </remarks>
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

    [TestMethod]
    public void Describe_AsksTheEnabledProjection_NotEveryLoadedProvider()
    {
        var source = Extract(
            Source("App/Helpers/CalendarTextService.cs"),
            "internal static string Describe(DateTime date)",
            "internal static string Describe(IEnumerable");

        Assert.Contains("PluginManager.Instance.CalendarTextProviders", source,
            "the host must ask the enabled projection, which is what keeps a disabled plugin from being called");
        Assert.DoesNotContain("AllCalendarTextProviders", source);
    }

    private static string Extract(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, start, $"could not find '{from}'");
        var end = source.IndexOf(to, start, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, end, $"could not find '{to}' after '{from}'");
        return source.Substring(start, end - start);
    }

    private static string Source(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");
        var path = Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.IsTrue(File.Exists(path), $"expected a file at {path}");
        return File.ReadAllText(path);
    }
}
