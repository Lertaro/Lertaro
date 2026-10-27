using Lertaro.PluginSdk.Helpers;

namespace Lertaro.PluginSdk.Tests.Helpers;

// The one table three surfaces read: the engine's per-row filter, the quick window's filter tabs, and the
// Core Extensions plugin's :@keyword rules. Pinned here because the two contributor tables this replaced had
// already drifted from each other (one listed .mts and .ogv, the other did not) inside a single change.
[TestClass]
public sealed class SearchCategoryClassifierTests
{
    [TestMethod]
    [DataRow("report.docx", SearchCategoryFilter.Document)]
    [DataRow("notes.wps", SearchCategoryFilter.Document)]
    [DataRow("budget.ods", SearchCategoryFilter.Document)]
    [DataRow("paper.tex", SearchCategoryFilter.Document)]
    [DataRow("photo.jxl", SearchCategoryFilter.Image)]
    [DataRow("icon.svg", SearchCategoryFilter.Image)]
    [DataRow("song.opus", SearchCategoryFilter.Audio)]
    [DataRow("clip.mts", SearchCategoryFilter.Video)]
    [DataRow("movie.ogv", SearchCategoryFilter.Video)]
    [DataRow("disk.iso", SearchCategoryFilter.Archive)]
    [DataRow("bundle.wim", SearchCategoryFilter.Archive)]
    [DataRow("setup.msi", SearchCategoryFilter.Application)]
    [DataRow("launch.cmd", SearchCategoryFilter.Application)]
    [DataRow("PORTABLE.RAR", SearchCategoryFilter.Archive)]
    public void Classify_NameByExtension_AnswersExactlyOneCategory(string name, SearchCategoryFilter expected) =>
        Assert.AreEqual(expected, SearchCategoryClassifier.Classify(name, isDirectory: false));

    // Formats the :@doc rule already claimed but the tabs' own table did not.
    [TestMethod]
    [DataRow("简历.et")]
    [DataRow("演示.dps")]
    [DataRow("book.epub")]
    public void Classify_FormatsTheDirectoryRulesAlreadyCovered_AreStillDocuments(string name) =>
        Assert.AreEqual(SearchCategoryFilter.Document, SearchCategoryClassifier.Classify(name, isDirectory: false));

    [TestMethod]
    [DataRow("readme.xyz")]
    [DataRow("Makefile")]
    [DataRow("archive.tar.gz.bak")]
    [DataRow("")]
    public void Classify_NothingToClaim_BelongsToNoCategory(string name) =>
        Assert.IsNull(SearchCategoryClassifier.Classify(name, isDirectory: false));

    [TestMethod]
    public void Classify_NullName_BelongsToNoCategory() =>
        Assert.IsNull(SearchCategoryClassifier.Classify(null, isDirectory: false));

    [TestMethod]
    public void Classify_ShortcutIsAPointer_ItsExtensionIsNotACategory() =>
        Assert.IsNull(SearchCategoryClassifier.Classify(@"Start Menu\Edge.lnk", isDirectory: false),
            "a shortcut's category is whatever its target is, and only the caller can read that");

    [TestMethod]
    [DataRow("ms-settings:network")]
    [DataRow("ms-settings:display")]
    [DataRow("SystemSettings.exe")]
    [DataRow("shell:AppsFolder\\windows.immersivecontrolpanel_cw5n1h2txyewy!Microsoft.Settings")]
    public void Classify_SettingsShapes_WinAgainstTheirOwnExtension(string path) =>
        Assert.AreEqual(SearchCategoryFilter.Settings, SearchCategoryClassifier.Classify(path, isDirectory: false),
            $"{path} would otherwise be filed as an application by its .exe/package name");

    [TestMethod]
    public void Classify_ADirectoryIsAFolderEvenWhenItIsNamedLikeAFile()
    {
        Assert.AreEqual(SearchCategoryFilter.Folder, SearchCategoryClassifier.Classify("videos.mp4", isDirectory: true));
        Assert.AreEqual(SearchCategoryFilter.Folder, SearchCategoryClassifier.Classify(null, isDirectory: true));
    }

    [TestMethod]
    public void Matches_All_AcceptsEveryRowIncludingTheUncategorized()
    {
        foreach (var name in new[] { "source.cs", "notes.lnk", "photo.png", "", null })
            Assert.IsTrue(SearchCategoryClassifier.Matches(SearchCategoryFilter.All, name, isDirectory: false),
                $"{name ?? "<null>"} must survive the default filter");
    }

    [TestMethod]
    [DataRow(SearchCategoryFilter.Application)]
    [DataRow(SearchCategoryFilter.Settings)]
    [DataRow(SearchCategoryFilter.Document)]
    [DataRow(SearchCategoryFilter.Image)]
    [DataRow(SearchCategoryFilter.Audio)]
    [DataRow(SearchCategoryFilter.Video)]
    [DataRow(SearchCategoryFilter.Archive)]
    [DataRow(SearchCategoryFilter.Folder)]
    public void Matches_AShortcutPassesEveryCategory(SearchCategoryFilter filter) =>
        Assert.IsTrue(SearchCategoryClassifier.Matches(filter, "Edge.lnk", isDirectory: false),
            "the walk cannot resolve a target, so it must not be the side that drops the row");

    [TestMethod]
    [DataRow(SearchCategoryFilter.Audio, "notes.docx")]
    [DataRow(SearchCategoryFilter.Image, "clip.mp4")]
    [DataRow(SearchCategoryFilter.Archive, "song.mp3")]
    [DataRow(SearchCategoryFilter.Application, "readme.xyz")]
    [DataRow(SearchCategoryFilter.Folder, "photo.png")]
    public void Matches_AClaimedRowOfAnotherCategory_IsRejected(SearchCategoryFilter filter, string name) =>
        Assert.IsFalse(SearchCategoryClassifier.Matches(filter, name, isDirectory: false));

    [TestMethod]
    public void Matches_AFolderPassesOnlyTheFolderCategory()
    {
        Assert.IsTrue(SearchCategoryClassifier.Matches(SearchCategoryFilter.Folder, @"D:\Downloads", isDirectory: true));
        Assert.IsFalse(SearchCategoryClassifier.Matches(SearchCategoryFilter.Document, @"D:\Downloads", isDirectory: true));
    }

    [TestMethod]
    [DataRow(SearchCategoryFilter.Application)]
    [DataRow(SearchCategoryFilter.Document)]
    [DataRow(SearchCategoryFilter.Image)]
    [DataRow(SearchCategoryFilter.Audio)]
    [DataRow(SearchCategoryFilter.Video)]
    [DataRow(SearchCategoryFilter.Archive)]
    public void RuleOf_TheGlobFormAgreesWithTheTable(SearchCategoryFilter category)
    {
        var extensions = RuleExtensions(category);
        Assert.IsTrue(extensions.Count > 0, $"{category} must have a rule a user can write");

        foreach (var extension in extensions)
            Assert.AreEqual(category, SearchCategoryClassifier.Classify("sample" + extension, isDirectory: false),
                $"{extension} is in the {category} rule but the table files it elsewhere");
    }

    [TestMethod]
    [DataRow(SearchCategoryFilter.All)]
    [DataRow(SearchCategoryFilter.Folder)]
    [DataRow(SearchCategoryFilter.Settings)]
    public void RuleOf_TheCategoriesNoGlobCanAnswer_HaveNoRule(SearchCategoryFilter category) =>
        Assert.AreEqual(string.Empty, SearchCategoryClassifier.RuleOf(category));

    // The 24 extensions the :@ rules gained from the table must not answer twice: one file, one category.
    [TestMethod]
    public void Tables_AreDisjointAcrossCategories()
    {
        var seen = new Dictionary<string, SearchCategoryFilter>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in new[]
                 {
                     SearchCategoryFilter.Application, SearchCategoryFilter.Document, SearchCategoryFilter.Image,
                     SearchCategoryFilter.Audio, SearchCategoryFilter.Video, SearchCategoryFilter.Archive,
                 })
        {
            foreach (var extension in RuleExtensions(category))
            {
                Assert.IsFalse(seen.TryGetValue(extension, out var previous),
                    $"{extension} is claimed by both {previous} and {category}");
                seen[extension] = category;
            }
        }
    }

    private static List<string> RuleExtensions(SearchCategoryFilter category) =>
        SearchCategoryClassifier.RuleOf(category)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(pattern => pattern.TrimStart('*'))
            .ToList();
}
