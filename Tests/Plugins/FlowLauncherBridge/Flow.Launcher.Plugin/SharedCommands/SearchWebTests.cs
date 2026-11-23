using Flow.Launcher.Plugin.SharedCommands;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Flow.Launcher.Plugin.SharedCommands;

[TestClass]
public sealed class SearchWebTests
{
    [TestMethod]
    [DataRow(false, false, "--profile=work https://example.test/")]
    [DataRow(true, true, "--new-window --private --profile=work https://example.test/")]
    public void BuildStartInfo_AppliesRequestedFlags(bool newWindow, bool inPrivate, string expected)
    {
        var info = SearchWeb.BuildStartInfo("https://example.test/", @"C:\Browser\browser.exe", inPrivate, "--private", "--profile=work", newWindow);
        Assert.AreEqual(expected, info.Arguments);
        Assert.IsTrue(info.UseShellExecute);
    }

    [TestMethod]
    public void BrowserMethods_KeepOldAndUpstreamSignatures()
    {
        foreach (var name in new[] { "OpenInBrowserTab", "OpenInBrowserWindow" })
        {
            Assert.IsNotNull(typeof(SearchWeb).GetMethod(name, [typeof(string), typeof(string), typeof(bool), typeof(string)]));
            Assert.IsNotNull(typeof(SearchWeb).GetMethod(name, [typeof(string), typeof(string), typeof(bool), typeof(string), typeof(string)]));
        }
    }
}
