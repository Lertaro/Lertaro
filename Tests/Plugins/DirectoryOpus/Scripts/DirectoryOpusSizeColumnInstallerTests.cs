using Lertaro.Plugins.DirectoryOpus.Scripts;

namespace Lertaro.Plugins.DirectoryOpus.Tests.Scripts;

// The guard on removing a deployed script. Turning the size column off has to take the script out of
// Directory Opus's own process, and the only thing that makes the file Lertaro's to delete is its header.
[TestClass]
public sealed class DirectoryOpusSizeColumnInstallerTests
{
    private const string ManagedHeader = "// Lertaro Directory Opus script integration. This file is managed by Lertaro.";

    [TestMethod]
    public void IsManagedByLertaro_RecognizesTheScriptItDeployed()
    {
        var deployed = DirectoryOpusSizeColumnScriptBuilder.Build(
            typeof(DirectoryOpusSizeColumnInstaller).Assembly, "Lertaro Size", "Indexed folder sizes", @"C:\Tools\lff.exe");

        Assert.IsTrue(DirectoryOpusSizeColumnInstaller.IsManagedByLertaro(deployed));
    }

    [TestMethod]
    [DataRow(ManagedHeader + "\r\nvar x = 1;", true)]
    [DataRow("\uFEFF" + ManagedHeader, true)]
    [DataRow("// Someone else's script that happens to size folders.\r\nvar x = 1;", false)]
    [DataRow("", false)]
    [DataRow("var LertaroSizeLabel = \"Lertaro Size\";", false)]
    public void IsManagedByLertaro_DependsOnlyOnTheHeaderLine(string content, bool expected) =>
        Assert.AreEqual(expected, DirectoryOpusSizeColumnInstaller.IsManagedByLertaro(content));
}
