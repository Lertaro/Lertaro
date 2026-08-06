using Lertaro.App.Services;
using Lertaro.Core;

namespace Lertaro.App.Tests.Services;

[TestClass]
public sealed class IsVirtualPathTests
{
    [TestMethod]
    public void IsVirtualPath_ClsidToken_ReturnsTrue() =>
        Assert.IsTrue(FileExecutor.IsVirtualPath(@"::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"));

    [TestMethod]
    public void IsVirtualPath_ShellPrefix_ReturnsTrue() =>
        Assert.IsTrue(FileExecutor.IsVirtualPath("shell:RecycleBinFolder"));

    [TestMethod]
    public void IsVirtualPath_ShellPrefixIsCaseInsensitive() =>
        Assert.IsTrue(FileExecutor.IsVirtualPath("SHELL:RecycleBinFolder"));

    [TestMethod]
    public void IsVirtualPath_RealFilePath_ReturnsFalse() =>
        Assert.IsFalse(FileExecutor.IsVirtualPath(@"C:\folder\file.txt"));
}

[TestClass]
public sealed class IsElevatableExecutableTests
{
    [TestMethod]
    [DataRow(@"C:\app.exe")]
    [DataRow(@"C:\script.bat")]
    [DataRow(@"C:\script.cmd")]
    [DataRow(@"C:\legacy.com")]
    [DataRow(@"C:\screensaver.scr")]
    [DataRow(@"C:\installer.msi")]
    [DataRow(@"C:\shortcut.lnk")]
    public void IsElevatableExecutable_KnownExecutableExtension_ReturnsTrue(string path) =>
        Assert.IsTrue(FileExecutor.IsElevatableExecutable(path));

    [TestMethod]
    public void IsElevatableExecutable_ExtensionMatchIsCaseInsensitive() =>
        Assert.IsTrue(FileExecutor.IsElevatableExecutable(@"C:\App.EXE"));

    [TestMethod]
    [DataRow(@"C:\document.txt")]
    [DataRow(@"C:\readme.md")]
    [DataRow(@"C:\noextension")]
    [DataRow(@"C:\archive.zip")]
    public void IsElevatableExecutable_DocumentExtension_ReturnsFalse(string path) =>
        Assert.IsFalse(FileExecutor.IsElevatableExecutable(path));
}

[TestClass]
public sealed class BuildStartInfoTests
{
    [TestMethod]
    public void BuildStartInfo_NotAdmin_LaunchesPathDirectlyWithNoVerb()
    {
        var info = FileExecutor.BuildStartInfo(@"C:\folder", isFile: false, asAdmin: false, associatedExe: null);

        Assert.AreEqual(@"C:\folder", info.FileName);
        Assert.IsTrue(info.UseShellExecute);
        Assert.AreEqual("", info.Verb);
    }

    [TestMethod]
    public void BuildStartInfo_NotAdminFile_LaunchesFileDirectly()
    {
        var info = FileExecutor.BuildStartInfo(@"C:\doc.txt", isFile: true, asAdmin: false, associatedExe: null);

        Assert.AreEqual(@"C:\doc.txt", info.FileName);
        Assert.AreEqual("", info.Verb);
    }

    [TestMethod]
    public void BuildStartInfo_AdminFolder_OpensElevatedCmdInThatDirectory()
    {
        var info = FileExecutor.BuildStartInfo(@"C:\folder", isFile: false, asAdmin: true, associatedExe: null);

        Assert.AreEqual("cmd.exe", info.FileName);
        Assert.AreEqual("runas", info.Verb);
        Assert.Contains(@"""C:\folder""", info.Arguments);
    }

    [TestMethod]
    public void BuildStartInfo_AdminExecutableFile_ElevatesTheFileItselfDirectly()
    {
        var info = FileExecutor.BuildStartInfo(@"C:\app.exe", isFile: true, asAdmin: true, associatedExe: null);

        Assert.AreEqual(@"C:\app.exe", info.FileName);
        Assert.AreEqual("runas", info.Verb);
    }

    [TestMethod]
    public void BuildStartInfo_AdminDocumentWithAssociation_ElevatesAssociatedExeWithFileAsArgument()
    {
        var info = FileExecutor.BuildStartInfo(@"C:\report.docx", isFile: true, asAdmin: true, associatedExe: @"C:\Program Files\Word\winword.exe");

        Assert.AreEqual(@"C:\Program Files\Word\winword.exe", info.FileName);
        Assert.AreEqual("runas", info.Verb);
        Assert.Contains(@"""C:\report.docx""", info.Arguments);
    }

    [TestMethod]
    public void BuildStartInfo_AdminDocumentWithNoAssociation_FallsBackToElevatedOpenWith()
    {
        var info = FileExecutor.BuildStartInfo(@"C:\mystery.xyz", isFile: true, asAdmin: true, associatedExe: null);

        Assert.AreEqual("OpenWith.exe", info.FileName);
        Assert.AreEqual("runas", info.Verb);
        Assert.Contains(@"""C:\mystery.xyz""", info.Arguments);
    }

    [TestMethod]
    public void BuildStartInfo_AdminDocumentWithEmptyAssociation_FallsBackToElevatedOpenWith()
    {
        var info = FileExecutor.BuildStartInfo(@"C:\mystery.xyz", isFile: true, asAdmin: true, associatedExe: "");

        Assert.AreEqual("OpenWith.exe", info.FileName);
    }

    [TestMethod]
    public void BuildStartInfo_NeverSetsWorkingDirectory()
    {
        // WorkingDirectory is applied by the caller separately (real Directory.Exists I/O), never here.
        var info = FileExecutor.BuildStartInfo(@"C:\doc.txt", isFile: true, asAdmin: false, associatedExe: null);

        Assert.IsEmpty(info.WorkingDirectory);
    }

    [TestMethod]
    public void BuildStartInfo_FolderWithDefaultFileManagerConfigured_LaunchesConfiguredManager()
    {
        var setting = new DefaultFileManagerSetting { Enabled = true, Path = @"C:\portable\TotalCMD64.exe", Parameter = "/O /T /R=%s" };

        var info = FileExecutor.BuildStartInfo(@"C:\Program Files\Folder", isFile: false, asAdmin: false, associatedExe: null, setting);

        Assert.AreEqual(@"C:\portable\TotalCMD64.exe", info.FileName);
        Assert.AreEqual("/O /T /R=\"C:\\Program Files\\Folder\"", info.Arguments);
    }

    [TestMethod]
    public void BuildStartInfo_FileWithDefaultFileManagerConfigured_StillOpensFileDirectly()
    {
        // GitHub issue #180's setting only ever redirects opening a FOLDER -- a file still needs its own
        // associated program.
        var setting = new DefaultFileManagerSetting { Enabled = true, Path = @"C:\portable\TotalCMD64.exe" };

        var info = FileExecutor.BuildStartInfo(@"C:\doc.txt", isFile: true, asAdmin: false, associatedExe: null, setting);

        Assert.AreEqual(@"C:\doc.txt", info.FileName);
    }

    [TestMethod]
    public void BuildStartInfo_DefaultFileManagerDisabled_FallsBackToShellExecute()
    {
        var setting = new DefaultFileManagerSetting { Enabled = false, Path = @"C:\portable\TotalCMD64.exe" };

        var info = FileExecutor.BuildStartInfo(@"C:\folder", isFile: false, asAdmin: false, associatedExe: null, setting);

        Assert.AreEqual(@"C:\folder", info.FileName);
        Assert.IsTrue(info.UseShellExecute);
    }
}

[TestClass]
public sealed class TryBuildDefaultFileManagerStartInfoTests
{
    [TestMethod]
    public void TryBuildDefaultFileManagerStartInfo_NullSetting_ReturnsNull() =>
        Assert.IsNull(FileExecutor.TryBuildDefaultFileManagerStartInfo(@"C:\folder", null));

    [TestMethod]
    public void TryBuildDefaultFileManagerStartInfo_Disabled_ReturnsNull() =>
        Assert.IsNull(FileExecutor.TryBuildDefaultFileManagerStartInfo(@"C:\folder", new DefaultFileManagerSetting { Enabled = false, Path = @"C:\tc.exe" }));

    [TestMethod]
    public void TryBuildDefaultFileManagerStartInfo_EnabledButEmptyPath_ReturnsNull() =>
        Assert.IsNull(FileExecutor.TryBuildDefaultFileManagerStartInfo(@"C:\folder", new DefaultFileManagerSetting { Enabled = true, Path = "" }));

    [TestMethod]
    public void TryBuildDefaultFileManagerStartInfo_EnabledWithPath_UsesShellExecute()
    {
        var info = FileExecutor.TryBuildDefaultFileManagerStartInfo(@"C:\folder", new DefaultFileManagerSetting { Enabled = true, Path = @"C:\tc.exe" });

        Assert.IsNotNull(info);
        Assert.IsTrue(info.UseShellExecute);
    }
}

[TestClass]
public sealed class BuildDefaultFileManagerArgumentsTests
{
    [TestMethod]
    public void BuildDefaultFileManagerArguments_PathWithSpaces_SubstitutesQuotedPath() =>
        // %s expands already-quoted (same %s/{} convention CustomActions.DynamicActionProvider.RunMulti
        // already uses) -- the user must not add their own quotes around it, since that would double up.
        Assert.AreEqual(
            "/O /T /R=\"C:\\Program Files\\Folder\"",
            FileExecutor.BuildDefaultFileManagerArguments(@"C:\Program Files\Folder", "/O /T /R=%s"));

    [TestMethod]
    public void BuildDefaultFileManagerArguments_PathWithNoSpaces_SubstitutesUnquotedPath() =>
        // ArgQuoting.Quote only adds quotes when actually needed -- a plain path with no whitespace/quote
        // characters is passed through bare.
        Assert.AreEqual("/O /T /R=C:\\a", FileExecutor.BuildDefaultFileManagerArguments(@"C:\a", "/O /T /R=%s"));

    [TestMethod]
    public void BuildDefaultFileManagerArguments_CurlyBracePlaceholder_AlsoSubstitutes() =>
        // {} is interchangeable with %s, same as CustomActions' own convention.
        Assert.AreEqual(
            "/O /T /R=\"C:\\Program Files\"",
            FileExecutor.BuildDefaultFileManagerArguments(@"C:\Program Files", "/O /T /R={}"));

    [TestMethod]
    public void BuildDefaultFileManagerArguments_MultiplePlaceholders_SubstitutesAll() =>
        Assert.AreEqual(
            "/L=\"C:\\Program Files\" /R=\"C:\\Program Files\"",
            FileExecutor.BuildDefaultFileManagerArguments(@"C:\Program Files", "/L=%s /R={}"));

    [TestMethod]
    public void BuildDefaultFileManagerArguments_EmptyTemplate_QuotesPathAsSoleArgument() =>
        Assert.AreEqual(@"""C:\Program Files""", FileExecutor.BuildDefaultFileManagerArguments(@"C:\Program Files", ""));

    [TestMethod]
    public void BuildDefaultFileManagerArguments_NullTemplate_QuotesPathAsSoleArgument() =>
        Assert.AreEqual(@"""C:\Program Files""", FileExecutor.BuildDefaultFileManagerArguments(@"C:\Program Files", null));
}
