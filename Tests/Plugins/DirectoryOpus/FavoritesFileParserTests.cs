using Lertaro.Plugins.DirectoryOpus.Favorites;

namespace Lertaro.Plugins.DirectoryOpus.Tests;

// Parsing of Directory Opus's favorites.ofv. Only the structural half is covered here: ParseXml does no
// filesystem access on purpose, so these run against literal XML rather than against whatever happens to
// exist on the machine.
[TestClass]
public sealed class FavoritesFileParserTests
{
    // A real favorites.ofv written by Opus with one of each of the six entry types its New menu offers:
    // Favorite Folder, Favorite File, Branch, Separator, Gap and Heading. Only the paths are changed.
    private const string RealWorldSample = """
        <?xml version="1.0" encoding="UTF-8"?>
        <favorites>
        	<folder>
        		<separator heading="aaaa" />
        	</folder>
        	<folder label="收藏栏" />
        	<folder>
        		<separator gap="yes" />
        	</folder>
        	<folder label="新分支" />
        	<path label="45">
        		<dir>
        			<pathstring>D:\Projects\Music</pathstring>
        			<pidl>?AAAAOgAfSAwm8RzQTbtOgR8zxXJpn94mAAEAJgDvvhEAAADUutk91</pidl>
        			<tree>64</tree>
        		</dir>
        	</path>
        	<folder>
        		<separator />
        	</folder>
        	<path label="aaa">
        		<file>
        			<pathstring>Z:\clips\demo.mp4</pathstring>
        		</file>
        	</path>
        </favorites>
        """;

    [TestMethod]
    public void EveryEntryTypeOfARealFileLandsAtTheTopLevel()
    {
        // The shape that broke the first version of this: Opus writes every separator and heading inside
        // an unlabelled <folder>. Read literally that is a nameless submenu, and all three would have
        // vanished. They belong beside the real entries, not inside anything.
        var nodes = FavoritesFileParser.ParseXml(RealWorldSample);

        Assert.HasCount(6, nodes);
        Assert.IsTrue(nodes[0].IsSeparator);
        Assert.AreEqual("aaaa", nodes[0].Label, "a heading keeps its text");
        Assert.AreEqual("收藏栏", nodes[1].Label);
        Assert.AreEqual("新分支", nodes[2].Label);
        Assert.AreEqual(@"D:\Projects\Music", nodes[3].Path);
        Assert.IsTrue(nodes[4].IsSeparator);
        Assert.AreEqual(@"Z:\clips\demo.mp4", nodes[5].Path);
    }

    [TestMethod]
    public void AGapIsDropped()
    {
        // Opus's "Gap" is blank vertical space on its favorites bar, which has room for it. A menu does
        // not, and drawing it as a line would invent a divider nobody asked for. The sample has one
        // between 收藏栏 and 新分支; those end up adjacent.
        var nodes = FavoritesFileParser.ParseXml(RealWorldSample);

        Assert.AreEqual("收藏栏", nodes[1].Label);
        Assert.AreEqual("新分支", nodes[2].Label);
    }

    [TestMethod]
    public void AFavoritedFileIsDistinguishedFromAFavoritedFolder()
    {
        // The one place this format differs from Total Commander's hotlist, and it changes the menu: a
        // directory opens a submenu of its contents, a file is a leaf that runs when clicked.
        var nodes = FavoritesFileParser.ParseXml(RealWorldSample);

        Assert.IsFalse(nodes[3].IsFile, "<dir> is a folder");
        Assert.IsTrue(nodes[5].IsFile, "<file> is a file");
    }

    [TestMethod]
    public void DividersLeftStrandedByPruningAreCleanedUp()
    {
        // What the real file turns into once the two empty branches are dropped: a heading, then two
        // plain dividers with nothing between them, then the entries. Opus showed something orderly, so
        // this should too.
        var nodes = FavoritesFileParser.ParseXml(RealWorldSample);
        FavoritesFileParser.Prune(nodes);
        FavoritesFileParser.TidyDividers(nodes);

        Assert.AreEqual("aaaa", nodes[0].Label, "a heading at the top is a normal way to title a group");
        for (var i = 1; i < nodes.Count; i++)
        {
            var doubled = nodes[i].IsSeparator && nodes[i].Label.Length == 0 && nodes[i - 1].IsSeparator;
            Assert.IsFalse(doubled, $"two dividers in a row at index {i}");
        }
        Assert.IsFalse(nodes[^1].IsSeparator, "a menu should not end on a divider");
    }

    [TestMethod]
    public void FoldersNestAndCarryTheirOwnChildren()
    {
        var nodes = FavoritesFileParser.ParseXml("""
            <favorites>
              <folder label="Outer">
                <folder label="Inner">
                  <path label="Deep"><dir><pathstring>D:\Projects\deep</pathstring></dir></path>
                </folder>
              </folder>
            </favorites>
            """);

        Assert.HasCount(1, nodes);
        var inner = nodes[0].Children![0];
        Assert.AreEqual("Inner", inner.Label);
        Assert.AreEqual("Deep", inner.Children![0].Label);
    }

    [TestMethod]
    public void AnEntryWithNoLabelFallsBackToItsLastPathSegment()
    {
        // The label attribute is optional and Opus shows the location itself when it is missing. The last
        // segment reads better in a menu than the whole path.
        var nodes = FavoritesFileParser.ParseXml("""
            <favorites>
              <path><dir><pathstring>D:\Projects\Reports</pathstring></dir></path>
            </favorites>
            """);

        Assert.AreEqual("Reports", nodes[0].Label);
    }

    [TestMethod]
    public void ADriveRootWithNoLabelKeepsTheWholePath()
    {
        // There is no last segment to fall back to, and an empty menu entry would be unclickable-looking.
        var nodes = FavoritesFileParser.ParseXml("""
            <favorites>
              <path><dir><pathstring>Z:\</pathstring></dir></path>
            </favorites>
            """);

        Assert.AreEqual(@"Z:\", nodes[0].Label);
    }

    [TestMethod]
    public void AVirtualLocationWithOnlyAPidlIsSkipped()
    {
        // How Opus stores This PC, the Recycle Bin and friends: a pidl and no pathstring. There is no path
        // to hand downstream, so showing it would mean a menu entry that cannot go anywhere.
        var nodes = FavoritesFileParser.ParseXml("""
            <favorites>
              <path label="This PC"><dir><pidl>?AAAAOgAf</pidl><tree>64</tree></dir></path>
              <path label="Real"><dir><pathstring>D:\Projects</pathstring></dir></path>
            </favorites>
            """);

        Assert.HasCount(1, nodes);
        Assert.AreEqual("Real", nodes[0].Label);
    }

    [TestMethod]
    public void AnUnrootedOrEmptyPathIsSkipped()
    {
        var nodes = FavoritesFileParser.ParseXml("""
            <favorites>
              <path label="Relative"><dir><pathstring>..\sibling</pathstring></dir></path>
              <path label="Blank"><dir><pathstring>   </pathstring></dir></path>
            </favorites>
            """);

        Assert.IsEmpty(nodes);
    }

    [TestMethod]
    public void SeparatorsSurviveAsSeparators()
    {
        // The heading attribute is dropped: a DynamicMenuItem separator carries no text.
        var nodes = FavoritesFileParser.ParseXml("""
            <favorites>
              <path label="A"><dir><pathstring>D:\Projects</pathstring></dir></path>
              <separator heading="Section" />
              <path label="B"><dir><pathstring>Z:\other</pathstring></dir></path>
            </favorites>
            """);

        Assert.HasCount(3, nodes);
        Assert.IsTrue(nodes[1].IsSeparator);
    }

    [TestMethod]
    public void PruneDropsFoldersLeftWithNothingInThem()
    {
        // A folder whose entries all failed to resolve would otherwise sit next to real ones and open onto
        // nothing. Applied at every depth, not just the root.
        var nodes = FavoritesFileParser.ParseXml("""
            <favorites>
              <folder label="Empty" />
              <folder label="OnlyEmptyChildren"><folder label="AlsoEmpty" /></folder>
              <folder label="Keeps">
                <path label="Real"><dir><pathstring>D:\Projects</pathstring></dir></path>
              </folder>
            </favorites>
            """);

        FavoritesFileParser.Prune(nodes);

        Assert.HasCount(1, nodes);
        Assert.AreEqual("Keeps", nodes[0].Label);
    }

    [TestMethod]
    public void PruneLeavesAnAllSeparatorListEmpty()
    {
        // Separators alone are not content: a submenu holding only dividers is still a dead end.
        var nodes = FavoritesFileParser.ParseXml("""
            <favorites>
              <separator />
              <separator />
            </favorites>
            """);

        FavoritesFileParser.Prune(nodes);

        Assert.IsEmpty(nodes);
    }

    [TestMethod]
    public void AnUnreadableFileYieldsNothingRatherThanThrowing()
    {
        // Opus rewrites this file when the user edits their favorites, so a read can land mid-save. One
        // popup showing no favorites is the right outcome; an exception out of a menu build is not.
        Assert.IsEmpty(FavoritesFileParser.ParseXml("<favorites><folder label=\"unclosed\">"));
        Assert.IsEmpty(FavoritesFileParser.ParseXml(""));
        Assert.IsEmpty(FavoritesFileParser.ParseXml("not xml at all"));
    }

    [TestMethod]
    public void UnknownElementsAreIgnoredRatherThanGuessedAt()
    {
        // Opus is free to add to this format; anything unrecognised is left out instead of being turned
        // into an entry that does something unintended.
        var nodes = FavoritesFileParser.ParseXml("""
            <favorites>
              <somethingnew label="?" />
              <path label="Real"><dir><pathstring>D:\Projects</pathstring></dir></path>
            </favorites>
            """);

        Assert.HasCount(1, nodes);
        Assert.AreEqual("Real", nodes[0].Label);
    }
}
