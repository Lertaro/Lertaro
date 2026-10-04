using Lertaro.Plugins.CoreExtensions.Providers.InstantAnswers;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.InstantAnswers;

// Whether a path "is there" is decided by the real filesystem and the real shell, so these cases are built
// on two things any Windows machine has: a folder this class creates under %TEMP% and deletes afterwards
// (an existing one), and a GUID-named sibling (a missing one).
//
// [DoNotParallelize] because the %VARIABLE% cases set and clear a process environment variable, and that
// environment block is shared by every test in the process -- see EnvironmentVariableInstantProviderTests
// for the failure this prevents.
[TestClass]
[DoNotParallelize]
public sealed class DirectoryJumpInstantProviderTests
{
    private const string TestVarName = "LERTARODIRJUMPVAR";

    private string _folder = string.Empty;
    private string _file = string.Empty;

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable(TestVarName, null, EnvironmentVariableTarget.Process);
        if (_folder.Length > 0) Directory.Delete(_folder, recursive: true);
        if (_file.Length > 0) File.Delete(_file);
    }

    // A folder of our own rather than %TEMP% itself: the separator cases need a tail segment to hang a
    // trailing separator off, and %TEMP% already ends with one.
    private string ExistingFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "lertaro_dirjump_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        return _folder;
    }

    private static bool Resolves(string? query) => DirectoryJumpInstantProvider.TryResolve(query, out _);

    [TestMethod]
    public void TryResolve_ExistingFolder_BackslashForm_IsAcceptedAsTyped()
    {
        var folder = ExistingFolder();

        Assert.IsTrue(DirectoryJumpInstantProvider.TryResolve(folder, out var resolved));
        Assert.AreEqual(folder, resolved);
    }

    [TestMethod]
    public void TryResolve_ForwardSlashesAndTrailingSeparator_AreNormalisedToOneSpelling()
    {
        var folder = ExistingFolder();

        Assert.IsTrue(DirectoryJumpInstantProvider.TryResolve(folder.Replace('\\', '/'), out var slashes));
        Assert.AreEqual(folder, slashes, "'/' is Windows' alternate separator, so both spellings are one path");

        Assert.IsTrue(DirectoryJumpInstantProvider.TryResolve(folder.Replace('\\', '/') + "/", out var trailing));
        Assert.AreEqual(folder + "\\", trailing, "a trailing separator is part of what the user typed, not noise");
    }

    [TestMethod]
    public void TryResolve_SurroundingQuotePair_IsNotPartOfThePath()
    {
        var folder = ExistingFolder();

        Assert.IsTrue(Resolves("\"" + folder + "\""));
        Assert.IsTrue(Resolves("'" + folder + "'"));
        Assert.IsFalse(Resolves(folder + "\""), "an unbalanced quote is ordinary text, so this names no folder");
    }

    [TestMethod]
    public void TryResolve_PathBuiltFromAVariable_ExpandsAndAccepts()
    {
        var folder = ExistingFolder();
        Environment.SetEnvironmentVariable(TestVarName, Path.GetDirectoryName(folder), EnvironmentVariableTarget.Process);

        var query = $"%{TestVarName}%/{Path.GetFileName(folder)}";
        Assert.IsTrue(DirectoryJumpInstantProvider.TryResolve(query, out var resolved));
        Assert.AreEqual(folder, resolved);
    }

    [TestMethod]
    public void TryResolve_UnknownVariable_IsRefusedAsATypo() =>
        Assert.IsFalse(Resolves(@"%NoSuchLertaroVarXyz123%\AppData\Local"));

    [TestMethod]
    public void TryResolve_BareVariable_IsLeftToTheEnvironmentVariableRow()
    {
        var folder = ExistingFolder();
        Environment.SetEnvironmentVariable(TestVarName, folder, EnvironmentVariableTarget.Process);

        // "what does this variable hold" is EnvironmentVariableInstantProvider's question, and it answers it
        // even for a folder -- this provider claiming it too would put two rows on one Enter.
        Assert.IsFalse(Resolves($"%{TestVarName}%"));
    }

    [TestMethod]
    public void TryResolve_MissingFolder_AnswersNothing() =>
        Assert.IsFalse(Resolves(Path.Combine(Path.GetTempPath(), "lertaro_missing_" + Guid.NewGuid().ToString("N"))));

    [TestMethod]
    public void TryResolve_ExistingFile_IsNotAFolder()
    {
        _file = Path.Combine(Path.GetTempPath(), "lertaro_dirjump_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(_file, "x");

        Assert.IsFalse(Resolves(_file), "this jumps to folders; a file stays an ordinary search string");
    }

    [TestMethod]
    public void TryResolve_ShellToken_AndItsChildren_AreAcceptedWithEitherSeparator()
    {
        // AppData\Local under the profile folder exists on every machine, and a shell token has to stay a
        // token for the shell to open it -- hence the expected value being what was typed.
        Assert.IsTrue(DirectoryJumpInstantProvider.TryResolve(@"shell:Profile\AppData\Local", out var back));
        Assert.AreEqual(@"shell:Profile\AppData\Local", back);

        Assert.IsTrue(DirectoryJumpInstantProvider.TryResolve("shell:Profile/AppData/Local", out var forward));
        Assert.AreEqual(back, forward, "the shell is not as forgiving about '/' as the filesystem APIs are");

        Assert.IsFalse(Resolves(@"shell:Profile\NoSuchLertaroFolderXyz"));
    }

    [TestMethod]
    [DataRow(@"C:\Users\me", true)]
    [DataRow(@"D:\", true)]
    [DataRow(@"\\server\share\x", true)]
    [DataRow(@"\\?\D:\x", true)]
    [DataRow("shell:Downloads", true)]
    [DataRow("SHELL:Downloads", true)]
    [DataRow("::{4234d49b-0245-4df3-b780-3893943456e1}", true)]
    [DataRow(@"C:", false)]
    [DataRow(@"C:Users\me", false)]
    [DataRow(@"\Users\me", false)]
    [DataRow(@"Core\SearchIndex", false)]
    [DataRow("readme report", false)]
    [DataRow("", false)]
    public void IsCompletePath_OnlyAbsoluteFormsQualify(string path, bool expected) =>
        Assert.AreEqual(expected, DirectoryJumpInstantProvider.IsCompletePath(path));

    [TestMethod]
    public void GetInstantResults_ExistingFolder_YieldsOneOpenRowForThatFolder()
    {
        var folder = ExistingFolder();

        var item = new DirectoryJumpInstantProvider().GetInstantResults(folder).Single();

        Assert.AreEqual("Execute", item.ActionType);
        Assert.AreEqual(folder, item.ActionArgument);
        Assert.IsNotNull(item.OnExecuteFunc, "a virtual or UNC target has no row path to open, so it goes through here");
    }

    [TestMethod]
    public void GetInstantResults_Prose_YieldsNothing()
    {
        Assert.IsEmpty(new DirectoryJumpInstantProvider().GetInstantResults("readme report"));
        Assert.IsEmpty(new DirectoryJumpInstantProvider().GetInstantResults(@"Core\SearchIndex"));
        Assert.IsEmpty(new DirectoryJumpInstantProvider().GetInstantResults(""));
    }

    [TestMethod]
    public void GetInstantResults_OtherQuerySyntax_YieldsNothing()
    {
        var provider = new DirectoryJumpInstantProvider();

        // None of these is "the whole query is one complete existing folder", so the drive spec, the regex
        // clause and path mode all keep their own reading with no row added on top. The UNC case is absent
        // on purpose: answering it needs a real share, and a bogus hostname would put a network timeout
        // inside the test run -- its shape is pinned by IsCompletePath instead.
        Assert.IsEmpty(provider.GetInstantResults("c:"));
        Assert.IsEmpty(provider.GetInstantResults("d:report"));
        Assert.IsEmpty(provider.GetInstantResults(@"d:\projects\ /^readme\.txt$/"));
        Assert.IsEmpty(provider.GetInstantResults(@"/^readme\.txt$/"));
        Assert.IsEmpty(provider.GetInstantResults(@"\audio 报告"));
    }
}
