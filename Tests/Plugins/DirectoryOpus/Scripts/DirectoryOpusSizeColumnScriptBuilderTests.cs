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
        StringAssert.Contains(script, "var result = DOpus.FSUtil.Run(QuoteArgument(LertaroCliPath) + \" --space-entries \" + QuoteArgument(directory), 0, \"r\");");
        StringAssert.Contains(script, "var entries = JSON.parse(result.stdout);");
        Assert.DoesNotContain("ActiveXObject", script, StringComparison.Ordinal);
        Assert.DoesNotContain("%TEMP%", script, StringComparison.Ordinal);
        StringAssert.Contains(script, "var itemPath = NormalizePath(String(item.realpath));");
        StringAssert.Contains(script, "var parentPath = NormalizePath(String(item.path));");
        Assert.DoesNotContain("delete LertaroSizeCache[\"\"];", script, StringComparison.Ordinal);
        StringAssert.Contains(script, "if (/^[a-z]:$/i.test(normalized))");
        StringAssert.Contains(script, "var LertaroCliPath = \"C:\\\\Tools\\\\lff.exe\";");
    }
}
