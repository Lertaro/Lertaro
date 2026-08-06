using Lertaro.PluginSdk.Shell.FileOperations;

namespace Lertaro.App.Tests.Views.QuickPanel;

// Where a name handed over by another process is allowed to land. The names in a drag's file group
// descriptor are written by whoever started the drag, so this is the one part of extracting them that
// has to be right rather than merely working.
[TestClass]
public sealed class VirtualFileExtractorTests
{
    private const string Target = @"C:\drop\inbox";

    [TestMethod]
    public void APlainName_LandsInTheFolder()
        => Assert.AreEqual(@"C:\drop\inbox\photo.png", VirtualFileExtractor.ResolveDestination(Target, "photo.png"));

    // A dragged folder describes its contents with the folder in front of each name, so a relative path
    // is legitimate and cannot simply be rejected.
    [TestMethod]
    public void ARelativePath_IsKept()
        => Assert.AreEqual(@"C:\drop\inbox\images\photo.png",
            VirtualFileExtractor.ResolveDestination(Target, @"images\photo.png"));

    [TestMethod]
    public void ClimbingOutOfTheFolder_IsRefused()
        => Assert.IsNull(VirtualFileExtractor.ResolveDestination(Target, @"..\..\Windows\System32\evil.dll"));

    // Path.Combine lets an absolute second argument replace the root outright, which is the same escape
    // by another route.
    [TestMethod]
    public void AnAbsoluteName_IsRefused()
        => Assert.IsNull(VirtualFileExtractor.ResolveDestination(Target, @"C:\Windows\System32\evil.dll"));

    // Climbing out and back in is still inside, so it is allowed -- the check is about where it lands,
    // not about how the name is spelled.
    [TestMethod]
    public void ClimbingOutAndBackIn_IsAllowed()
        => Assert.AreEqual(@"C:\drop\inbox\photo.png",
            VirtualFileExtractor.ResolveDestination(Target, @"..\inbox\photo.png"));

    // The folder itself is not a place a file can land, and neither is nothing at all.
    [TestMethod]
    public void ANameThatResolvesToTheFolderItself_IsRefused()
        => Assert.IsNull(VirtualFileExtractor.ResolveDestination(Target, @"..\inbox"));

    [TestMethod]
    public void NoNameOrNoFolder_IsRefused()
    {
        Assert.IsNull(VirtualFileExtractor.ResolveDestination(Target, "   "));
        Assert.IsNull(VirtualFileExtractor.ResolveDestination("   ", "photo.png"));
    }

    // One unusable descriptor must not cost the rest of the drag, so this answers null rather than
    // throwing.
    [TestMethod]
    public void AnUnusableName_IsRefusedRatherThanThrown()
        => Assert.IsNull(VirtualFileExtractor.ResolveDestination(Target, "bad\0name.png"));
}
