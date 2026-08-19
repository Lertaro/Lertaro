using Lertaro.Plugins.DirectoryOpus.Scripts;

namespace Lertaro.Plugins.DirectoryOpus.Tests.Scripts;

[TestClass]
public sealed class DirectoryOpusSizeColumnScriptBuilderTests
{
    [TestMethod]
    public void Render_ReplacesEveryTokenAndEscapesJScriptStrings()
    {
        const string template = "label={{LABEL}};description={{DESCRIPTION}};path={{LFF_PATH}};";

        var script = DirectoryOpusSizeColumnScriptBuilder.Render(template, "Lertaro \"Size\"", "\u5927\u5c0f\nLine two", @"C:\Tools\lff.exe");

        Assert.AreEqual("label=Lertaro \\\"Size\\\";description=\\u5927\\u5C0F\\nLine two;path=C:\\\\Tools\\\\lff.exe;", script);
        Assert.IsTrue(script.All(character => character <= 0x7f));
    }

    [TestMethod]
    public void Build_LoadsTheEmbeddedScriptTemplate()
    {
        var script = DirectoryOpusSizeColumnScriptBuilder.Build(
            typeof(DirectoryOpusSizeColumnScriptBuilder).Assembly,
            "Lertaro Size",
            "Indexed folder sizes",
            @"C:\Tools\lff.exe");

        StringAssert.Contains(script, "var LertaroSizeLabel = \"Lertaro Size\";");
        StringAssert.Contains(script, "initData.name = LertaroSizeLabel;");
        StringAssert.Contains(script, "function ReportError(error) {");
        StringAssert.Contains(script, "function ReadRunError(result) {");
        StringAssert.Contains(script, "function RunLertaroCli(commandLine) {");
        StringAssert.Contains(script, "if (DOpus.version.major >= 13)");
        StringAssert.Contains(script, "return DOpus.FSUtil.Run(commandLine, 0, \"r\");");
        StringAssert.Contains(script, "var shell = new ActiveXObject(\"WScript.Shell\");");
        StringAssert.Contains(script, "var tempDirectory = shell.ExpandEnvironmentStrings(\"%TEMP%\");");
        StringAssert.Contains(script, "var stream = new ActiveXObject(\"ADODB.Stream\");");
        StringAssert.Contains(script, "var entries = JSON.parse(result.stdout);");
        StringAssert.Contains(script, "DeleteFile(stdoutPath, fileSystem);");
        StringAssert.Contains(script, "DeleteFile(stderrPath, fileSystem);");
        StringAssert.Contains(script, "var itemPath = NormalizePath(String(item.realpath));");
        StringAssert.Contains(script, "var parentPath = NormalizePath(String(item.path));");
        Assert.DoesNotContain("delete LertaroSizeCache[\"\"];", script, StringComparison.Ordinal);
        StringAssert.Contains(script, "if (/^[a-z]:$/i.test(normalized))");
        StringAssert.Contains(script, "return AppendBackslashes(quoted, slashCount * 2) + \"\\\"\";");
        StringAssert.Contains(script, "var LertaroCliPath = \"C:\\\\Tools\\\\lff.exe\";");
    }
}
