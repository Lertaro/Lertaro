using Lertaro.Core.Services.Network;

namespace Lertaro.Core.Tests.Services.Network;

[TestClass]
public sealed class IndexedPathSpellingTests
{
    // One share mapped to Z:, plus a sibling whose name starts with the same text.
    private static readonly (string Unc, string Letter)[] Mappings =
    {
        (@"\\server\share", "Z"),
        (@"\\server\other", "T"),
    };

    private static string? Alternate(string path) => IndexedPathSpelling.AlternateSpelling(path, () => Mappings);

    private static string? AlternateWithNoMappings(string path) =>
        IndexedPathSpelling.AlternateSpelling(path, () => Array.Empty<(string, string)>());

    [TestMethod]
    public void AlternateSpelling_UncUnderAMappedShare_BecomesTheDriveLetterForm()
    {
        Assert.AreEqual(@"Z:\Movies\2024", Alternate(@"\\server\share\Movies\2024"));
        Assert.AreEqual(@"T:\docs", Alternate(@"\\SERVER\OTHER\docs"));
    }

    [TestMethod]
    public void AlternateSpelling_ShareRootItself_BecomesTheRootedDriveLetter() =>
        // "Z:" alone is a relative path (the drive's current directory), not the drive root.
        Assert.AreEqual(@"Z:\", Alternate(@"\\server\share"));

    [TestMethod]
    public void AlternateSpelling_MappedDriveLetter_BecomesTheUncForm()
    {
        Assert.AreEqual(@"\\server\share\Movies", Alternate(@"Z:\Movies"));
        Assert.AreEqual(@"\\server\share\", Alternate(@"Z:\"));
    }

    // A share must not claim a sibling that merely starts with its name, or an enumeration would be
    // answered from an index covering an entirely different directory.
    [TestMethod]
    public void AlternateSpelling_SiblingShareWithTheSamePrefix_IsNotRewritten()
    {
        Assert.IsNull(Alternate(@"\\server\share2\Movies"));
        Assert.IsNull(Alternate(@"\\server\shareholders"));
    }

    [TestMethod]
    public void AlternateSpelling_UnmappedUncOrDrive_HasNoAlternate()
    {
        Assert.IsNull(Alternate(@"\\elsewhere\share\Movies"));
        Assert.IsNull(Alternate(@"D:\Movies"));
        Assert.IsNull(AlternateWithNoMappings(@"\\server\share\Movies"));
        Assert.IsNull(AlternateWithNoMappings(@"Z:\Movies"));
    }

    // \\wsl$\ and \\wsl.localhost\ are two names for the same distro share; the index is keyed by the
    // first, but a folder index could have been configured under either.
    [TestMethod]
    public void AlternateSpelling_WslAliases_MapToEachOther()
    {
        Assert.AreEqual(@"\\wsl$\Ubuntu\home\me", Alternate(@"\\wsl.localhost\Ubuntu\home\me"));
        Assert.AreEqual(@"\\wsl.localhost\Ubuntu\home\me", Alternate(@"\\wsl$\Ubuntu\home\me"));
    }

    // The mapping table is a P/Invoke sweep per network drive -- a WSL path must never trigger it, and
    // neither must a plain local path.
    [TestMethod]
    public void AlternateSpelling_WslOrLocalPath_NeverReadsTheMappingTable()
    {
        static IReadOnlyList<(string, string)> Explode() => throw new InvalidOperationException("mappings must not be read here");

        Assert.AreEqual(@"\\wsl$\Ubuntu\home", IndexedPathSpelling.AlternateSpelling(@"\\wsl.localhost\Ubuntu\home", Explode));
        Assert.IsNull(IndexedPathSpelling.AlternateSpelling(@"Movies\2024", Explode));
        Assert.IsNull(IndexedPathSpelling.AlternateSpelling(string.Empty, Explode));
    }

    [TestMethod]
    public void IndexSpellings_PathWithNoAlternate_IsTheOnlySpellingTried() => CollectionAssert.AreEqual(new[] { @"\\elsewhere\share\Movies" }, IndexedPathSpelling.IndexSpellings(@"\\elsewhere\share\Movies").ToArray());

    [TestMethod]
    public void IndexSpellings_WslPath_TriesTheGivenSpellingFirstThenItsAlias() => CollectionAssert.AreEqual(
            new[] { @"\\wsl.localhost\Ubuntu\home", @"\\wsl$\Ubuntu\home" },
            IndexedPathSpelling.IndexSpellings(@"\\wsl.localhost\Ubuntu\home").ToArray());
}
