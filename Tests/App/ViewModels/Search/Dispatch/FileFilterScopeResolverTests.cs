using Lertaro.App.ViewModels.Search.Dispatch;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.ViewModels.Search.Dispatch;

// Pins the activation rules of the "tf report" leading-keyword scope syntax: the first token must
// hit a registered keyword and be followed by a space, everything after it is the searched term,
// and only index-covered folders make it into the directive. FileFilterScopeResolver.Resolve itself
// (PluginManager + SearchScopeCoverage wiring) is deliberately not exercised here.
[TestClass]
public sealed class FileFilterScopeResolverTests
{
    private static readonly SearchScope TfFilter = new()
    {
        Keyword = "tf",
        Folders = [@"C:\Movies", @"D:\Books"],
        FilterPattern = "*.mp4"
    };

    private static Dictionary<string, SearchScope> Scopes(params SearchScope[] scopes) =>
        scopes.ToDictionary(s => s.Keyword, s => s, StringComparer.OrdinalIgnoreCase);

    private static FileFilterScopeDirective? Match(string query, Dictionary<string, SearchScope> scopes, string[] coveredFolders, out string remainder) =>
        FileFilterScopeResolver.Match(query, scopes, folder => coveredFolders.Contains(folder, StringComparer.OrdinalIgnoreCase), out remainder);

    [TestMethod]
    public void QueryWithoutSpace_DoesNotActivate()
    {
        Assert.IsNull(Match("tf", Scopes(TfFilter), ["C:\\Movies"], out var remainder));
        Assert.AreEqual("tf", remainder);
    }

    [TestMethod]
    public void UnknownKeyword_DoesNotActivate() => Assert.IsNull(Match("xyz report", Scopes(TfFilter), ["C:\\Movies"], out _));

    [TestMethod]
    public void LeadingSpace_DoesNotActivate() => Assert.IsNull(Match(" tf report", Scopes(TfFilter), ["C:\\Movies"], out _));

    [TestMethod]
    public void KeywordPlusTerm_Activates_WithTrimmedRemainder()
    {
        var directive = Match("tf  report 2024 ", Scopes(TfFilter), ["C:\\Movies", "D:\\Books"], out var remainder);

        Assert.IsNotNull(directive);
        Assert.AreEqual("report 2024", remainder);
        CollectionAssert.AreEquivalent(new[] { @"C:\Movies", @"D:\Books" }, directive.Folders.ToList());
        Assert.AreEqual("*.mp4", directive.FilterPattern);
    }

    [TestMethod]
    public void KeywordMatch_IsCaseInsensitive()
    {
        var directive = Match("TF report", Scopes(TfFilter), ["C:\\Movies"], out var remainder);

        Assert.IsNotNull(directive);
        Assert.AreEqual("report", remainder);
    }

    [TestMethod]
    public void KeywordWithNoTerm_Activates_WithEmptyRemainder()
    {
        var directive = Match("tf ", Scopes(TfFilter), ["C:\\Movies"], out var remainder);

        Assert.IsNotNull(directive, "an activated scope with no term yet is the caller's keep-typing prompt signal");
        Assert.AreEqual(string.Empty, remainder);
    }

    [TestMethod]
    public void FullyUncoveredFolders_DoNotActivate_AtAll()
    {
        Assert.IsNull(Match("tf report", Scopes(TfFilter), [], out var remainder));
        Assert.AreEqual("tf report", remainder, "an unactivated query must be searched as typed");
    }

    [TestMethod]
    public void PartialCoverage_KeepsOnlyCoveredFolders()
    {
        var directive = Match("tf report", Scopes(TfFilter), ["D:\\Books"], out _);

        Assert.IsNotNull(directive);
        CollectionAssert.AreEquivalent(new[] { @"D:\Books" }, directive.Folders.ToList());
    }

    [TestMethod]
    public void BlankFolderEntries_AreDropped_AndDuplicatesCollapse()
    {
        var filter = new SearchScope { Keyword = "tf", Folders = ["", "  ", @"C:\Movies", @"c:\movies\"], FilterPattern = "" };
        var directive = Match("tf report", Scopes(filter), ["C:\\Movies"], out _);

        Assert.IsNotNull(directive);
        Assert.HasCount(1, directive.Folders);
        // A blank configured pattern means "everything", same as the filter's own default.
        Assert.AreEqual("*", directive.FilterPattern);
    }

    [TestMethod]
    public void EmptyQuery_DoesNotActivate() => Assert.IsNull(Match(string.Empty, Scopes(TfFilter), ["C:\\Movies"], out _));
}
