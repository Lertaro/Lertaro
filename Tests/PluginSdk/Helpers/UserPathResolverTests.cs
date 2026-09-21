using Lertaro.PluginSdk.Helpers;

namespace Lertaro.PluginSdk.Tests.Helpers;

// The pure half of the path resolution contract: which inputs count as shell tokens, how environment
// references are handled, and what reaches the shell lookup. The lookup itself is COM-backed and is
// covered by ShellPathHelperTests, which pins properties that hold on any Windows machine.
[TestClass]
public sealed class UserPathResolverTests
{
    [TestMethod]
    public void Expand_ResolvesEnvironmentReferences()
    {
        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");

        Assert.IsNotNull(systemRoot, "the fixture needs a variable Windows always sets");
        Assert.AreEqual(systemRoot, UserPathResolver.Expand("%SystemRoot%"));
    }

    [TestMethod]
    public void Expand_TrimsSurroundingWhitespace() =>
        Assert.AreEqual(
            Environment.GetEnvironmentVariable("SystemRoot"),
            UserPathResolver.Expand("   %SystemRoot%   "));

    // A plain path has nothing to expand and must come back byte for byte, trailing separator included:
    // every caller passes the result straight to a filesystem API.
    [TestMethod]
    public void Expand_LeavesAPlainPathAlone()
    {
        Assert.AreEqual(@"Z:\Apps\", UserPathResolver.Expand(@"Z:\Apps\"));
        Assert.AreEqual(@"Z:\Apps", UserPathResolver.Expand(@"  Z:\Apps  "));
    }

    [TestMethod]
    public void Expand_Blank_ReturnsTheInputAsItCameIn()
    {
        Assert.AreEqual(string.Empty, UserPathResolver.Expand(null));
        Assert.AreEqual(string.Empty, UserPathResolver.Expand(string.Empty));
        Assert.AreEqual("   ", UserPathResolver.Expand("   "));
    }

    [TestMethod]
    public void IsVirtualPath_SeparatesShellTokensFromPaths()
    {
        Assert.IsTrue(UserPathResolver.IsVirtualPath("shell:Downloads"));
        Assert.IsTrue(UserPathResolver.IsVirtualPath("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"));
        Assert.IsFalse(UserPathResolver.IsVirtualPath(@"Z:\Downloads"));
        Assert.IsFalse(UserPathResolver.IsVirtualPath(@"\\?\Z:\Downloads"));
        Assert.IsFalse(UserPathResolver.IsVirtualPath(null));
        Assert.IsFalse(UserPathResolver.IsVirtualPath("   "));
    }

    // The shape of the case bug this pins: the token prefix is shell syntax and is not case-sensitive, so
    // a caller writing "SHELL:Downloads" must be classified and resolved exactly like "shell:Downloads".
    // Classification and resolution used to disagree, because only one of them ignored case.
    [TestMethod]
    public void IsVirtualPath_IgnoresTheCaseOfTheTokenPrefix()
    {
        Assert.IsTrue(UserPathResolver.IsVirtualPath("SHELL:Downloads"));
        Assert.IsTrue(UserPathResolver.IsVirtualPath("Shell:Downloads"));
        Assert.IsTrue(UserPathResolver.IsVirtualPath("  shell:Downloads  "));
    }

    // Resolve hands the EXPANDED path to the shell lookup, which is what lets the lookup take one spelling
    // of a token. The seam stands in for the real call, which no unit test can drive.
    [TestMethod]
    public void Resolve_PassesTheExpandedPathToTheShellLookup()
    {
        var seen = new List<string>();

        var resolved = UserPathResolver.Resolve("  %SystemRoot%  ", path =>
        {
            seen.Add(path);
            return "resolved:" + path;
        });

        CollectionAssert.AreEqual(new[] { Environment.GetEnvironmentVariable("SystemRoot")! }, seen);
        Assert.AreEqual("resolved:" + Environment.GetEnvironmentVariable("SystemRoot"), resolved);
    }

    // A non-virtual path still goes through the lookup: the seam owns "nothing to resolve, return it as
    // is", and a caller-supplied resolver must see the same input the real one would.
    [TestMethod]
    public void Resolve_HandsANonVirtualPathToTheLookupToo() => Assert.AreEqual(@"Z:\Apps", UserPathResolver.Resolve(@"  Z:\Apps  ", path => path));

    [TestMethod]
    public void ResolveForNavigation_PreservesVirtualPathWhenItHasNoPhysicalPath()
    {
        const string input = "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}";

        Assert.AreEqual(input, UserPathResolver.ResolveForNavigation(input, _ => "::{679f85cb-0220-4080-b29b-5540cc05aab6}"));
    }

    [TestMethod]
    public void ResolveForNavigation_UsesResolvedPhysicalPath()
    {
        Assert.AreEqual(@"C:\Users\test\Desktop",
            UserPathResolver.ResolveForNavigation("shell:Desktop", _ => @"C:\Users\test\Desktop"));
    }
}
