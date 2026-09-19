using Lertaro.PluginSdk.Helpers;

namespace Lertaro.PluginSdk.Tests.Helpers;

// Virtual-path resolution against the real shell. These assert PROPERTIES rather than paths -- a resolved
// folder must exist, two spellings of one virtual folder must agree -- so they hold on any Windows machine
// and never pin this one's profile path.
[TestClass]
public sealed class ShellPathHelperTests
{
    // The case bug, stated as the invariant that was broken: the predicate the resolver uses and the one
    // callers use are now the same predicate, so they cannot answer differently again.
    [TestMethod]
    public void IsVirtualShellPath_AgreesWithUserPathResolver()
    {
        var inputs = new[]
        {
            "shell:Downloads", "SHELL:Downloads", "Shell:Downloads", "  shell:Downloads  ",
            "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", "shell:::{4234d49b-0245-4df3-b780-3893943456e1}",
            @"Z:\Apps", @"\\?\Z:\Apps", "", "   ",
        };

        foreach (var input in inputs)
            Assert.AreEqual(
                ShellPathHelper.IsVirtualShellPath(input),
                UserPathResolver.IsVirtualPath(input),
                $"classification disagreed for '{input}'");
    }

    [TestMethod]
    public void IsVirtualShellPath_RejectsNonTokens()
    {
        Assert.IsFalse(ShellPathHelper.IsVirtualShellPath(@"Z:\Apps"));
        Assert.IsFalse(ShellPathHelper.IsVirtualShellPath("shell"));
        Assert.IsFalse(ShellPathHelper.IsVirtualShellPath("shelldownloads"));
        Assert.IsFalse(ShellPathHelper.IsVirtualShellPath(":"));
        Assert.IsFalse(ShellPathHelper.IsVirtualShellPath(null));
        Assert.IsFalse(ShellPathHelper.IsVirtualShellPath("  "));
    }

    // A token with a physical folder behind it resolves to that folder, whatever case it was written in.
    // shell:Profile is used because every Windows account has one, and the assertion is "it exists and is
    // no longer virtual" rather than any particular path.
    [TestMethod]
    public void TryResolveVirtualPath_FolderWithAPhysicalPath_ResolvesToIt()
    {
        var resolved = ShellPathHelper.TryResolveVirtualPath("shell:Profile");

        Assert.IsFalse(ShellPathHelper.IsVirtualShellPath(resolved), $"'{resolved}' should be a real path");
        Assert.IsTrue(Directory.Exists(resolved), $"'{resolved}' should exist");
    }

    // The reported gap: a token written in another case reached the shell unresolved, because only this
    // side compared the prefix case-sensitively while IsVirtualPath did not.
    [TestMethod]
    public void TryResolveVirtualPath_CaseVariant_ResolvesLikeTheCanonicalSpelling()
    {
        var canonical = ShellPathHelper.TryResolveVirtualPath("shell:Profile");

        Assert.AreEqual(canonical, ShellPathHelper.TryResolveVirtualPath("SHELL:Profile"));
        Assert.AreEqual(canonical, ShellPathHelper.TryResolveVirtualPath("Shell:Profile"));
        Assert.AreEqual(canonical, ShellPathHelper.TryResolveVirtualPath("  shell:Profile  "));
    }

    // A folder that lives only inside the shell namespace has no physical path to return, and used to come
    // back as whichever token the caller wrote. Its canonical shell name is the answer the shell CAN give,
    // and it is what makes two spellings of one folder comparable.
    [TestMethod]
    public void TryResolveVirtualPath_PathlessFolder_ResolvesToItsCanonicalShellName()
    {
        var fromToken = ShellPathHelper.TryResolveVirtualPath("shell:appsfolder");
        var fromClsid = ShellPathHelper.TryResolveVirtualPath("shell:::{4234d49b-0245-4df3-b780-3893943456e1}");

        Assert.StartsWith("::{", fromToken, $"'{fromToken}' should be a canonical shell name");
        Assert.IsTrue(ShellPathHelper.IsVirtualShellPath(fromToken), "a canonical shell name is still virtual");
        Assert.AreEqual(fromToken, fromClsid, "both spellings must collapse onto one name");
    }

    // A canonical name is an acceptable INPUT too, and resolves to itself rather than drifting.
    [TestMethod]
    public void TryResolveVirtualPath_CanonicalName_IsIdempotent()
    {
        var once = ShellPathHelper.TryResolveVirtualPath("shell:appsfolder");

        Assert.AreEqual(once, ShellPathHelper.TryResolveVirtualPath(once));
    }

    // Nothing the shell can parse: a typo must stay exactly as typed, so a caller's fallback can show the
    // user what they actually wrote.
    [TestMethod]
    public void TryResolveVirtualPath_UnparseableToken_IsReturnedUnchanged()
    {
        const string typo = "shell:lertaro-no-such-folder-7f3a1c";

        Assert.AreEqual(typo, ShellPathHelper.TryResolveVirtualPath(typo));
    }

    // Not a shell token at all: this method's job is virtual paths, and a real path must not be touched
    // (a "\\?\" path in particular must survive, since it is already the long-path form).
    [TestMethod]
    public void TryResolveVirtualPath_NonVirtualInput_IsReturnedUnchanged()
    {
        Assert.AreEqual(@"Z:\Apps", ShellPathHelper.TryResolveVirtualPath(@"Z:\Apps"));
        Assert.AreEqual(@"\\?\Z:\Apps", ShellPathHelper.TryResolveVirtualPath(@"\\?\Z:\Apps"));
        Assert.AreEqual(string.Empty, ShellPathHelper.TryResolveVirtualPath(string.Empty));
    }
}
