using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Plugins.DirectoryOpus.Tests;

// The translation Directory Opus's window text needs: the container title and the XML display_path are
// BOTH localized ("C:\用户\..." for "C:\Users\..."), and that spelling is not a path that exists.
//
// Every case below is built from THIS machine's own shell display name rather than a hardcoded Chinese
// string, so the test pins the behaviour on an English install, a Chinese one and everything between:
// where the display name happens to equal the real name the "localized" input is simply the real path,
// and the assertions still hold.
[TestClass]
public sealed class LocalizedPathResolverTests
{
    private static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // The drive's "Users" folder and its localized spelling, which is "用户" on a Chinese Windows. Note
    // this is the PARENT of the profile: the profile's own localized name is just the account name.
    private static readonly string UsersRoot = Path.GetDirectoryName(UserProfile)!;

    private static readonly string LocalizedUsersRoot =
        Path.Combine(Path.GetPathRoot(UserProfile)!, ShellPathHelper.GetLocalizedFolderName(UsersRoot));

    // The localized spelling of the profile folder itself, ready to be recombined into longer paths.
    private static string LocalizedProfile => Path.Combine(LocalizedUsersRoot, Path.GetFileName(UserProfile));

    [TestMethod]
    public void Resolve_TranslatesALocalizedPathBackToTheRealOne()
    {
        var translated = LocalizedPathResolver.Resolve(
            Path.Combine(LocalizedProfile, "AppData", "Local", "Temp"));

        Assert.AreEqual(Path.Combine(UserProfile, "AppData", "Local", "Temp"), translated);
        Assert.IsTrue(Directory.Exists(translated), $"'{translated}' should be the real, existing path");
    }

    // The overwhelmingly common case: nothing in the path is localized, so it must come back byte for
    // byte -- including a real folder whose name genuinely contains non-ASCII characters.
    [TestMethod]
    public void Resolve_LeavesARealPathUntouched()
    {
        Assert.AreEqual(UserProfile, LocalizedPathResolver.Resolve(UserProfile));
        Assert.AreEqual(@"C:\Windows", LocalizedPathResolver.Resolve(@"C:\Windows"));
        Assert.AreEqual(@"C:\", LocalizedPathResolver.Resolve(@"C:\"));
        Assert.AreEqual(@"D:\2013.3.31 上坟", LocalizedPathResolver.Resolve(@"D:\2013.3.31 上坟"));
    }

    // A path with no real counterpart must be handed back unchanged: the caller asked "what does this
    // name", and "no idea, here is what you gave me" is the only honest answer. Note the second case
    // would resolve its first segment and then fail -- a half-translated path would be worse than none.
    [TestMethod]
    public void Resolve_ReturnsTheInputWhenItCannotBeTranslated()
    {
        var missing = Path.Combine(LocalizedProfile, "lertaro-no-such-folder-7f3a1c");
        Assert.AreEqual(missing, LocalizedPathResolver.Resolve(missing));
    }

    // Not a filesystem path at all: shell virtual names ("::{20D04FE0-...}") and bare special-folder
    // names are the other resolvers' business, so this one must not touch them.
    [TestMethod]
    public void Resolve_LeavesNonRootedAndVirtualPathsAlone()
    {
        Assert.AreEqual("Desktop", LocalizedPathResolver.Resolve("Desktop"));
        Assert.AreEqual(@"::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", LocalizedPathResolver.Resolve(@"::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"));
        Assert.AreEqual(string.Empty, LocalizedPathResolver.Resolve(string.Empty));
    }
}
