using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Lertaro.App.Services.Plugin;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

[TestClass]
[DoNotParallelize]
public sealed class CommandExecutionContextTests
{
    private readonly Func<string, string, object?, object?>? _oldSettings = PluginSettingsService.GetSettingFunc;

    [TestCleanup]
    public void Cleanup() => PluginSettingsService.GetSettingFunc = _oldSettings;

    [TestMethod]
    public void AddInstantResults_CapturesDirectoryForProviderAndExecution()
    {
        PluginSettingsService.GetSettingFunc = null;
        var provider = new ContextProvider();
        var rows = new List<AppSearchResult>();
        PluginSearchResultMapper.AddInstantResults(rows, "$ dir", null, true, @"D:\original", [provider]);
        var item = Assert.ContainsSingle(rows);
        Assert.AreEqual(@"D:\original", item.ContextDirectory);
        Assert.IsNull(provider.ExecutedDirectory);
        PluginSearchResultMapper.AddInstantResults([], "$ dir", null, true, @"E:\later", [provider]);
        item.InstantResultOnExecute!();
        Assert.AreEqual(@"D:\original", provider.ExecutedDirectory);
    }

    [TestMethod]
    public void AddInstantResults_LegacyProviderStillReceivesQuery()
    {
        var provider = new LegacyProvider();
        var rows = new List<AppSearchResult>();
        PluginSearchResultMapper.AddInstantResults(rows, "legacy", null, false, @"D:\source", [provider]);
        Assert.AreEqual("legacy", Assert.ContainsSingle(rows).Name);
        Assert.AreEqual(1, provider.Calls);
    }

    [TestMethod]
    [DataRow(true, 1)]
    [DataRow(false, 0)]
    public void AddInstantResults_InlineRequiresOptInAndEnabledSetting(bool enabled, int count)
    {
        PluginSettingsService.GetSettingFunc = (_, key, fallback) => key == "InlineSearchEnableSearchActions" ? enabled : fallback;
        var legacy = new LegacyProvider();
        var rows = new List<AppSearchResult>();
        PluginSearchResultMapper.AddInstantResults(rows, "query", null, true, @"D:\source", [legacy, new ContextProvider()]);
        Assert.HasCount(count, rows);
        Assert.AreEqual(0, legacy.Calls);
    }

    [TestMethod]
    public void BuildCustomCommandStartInfo_LegacyPayloadPreservesOptions()
    {
        using var doc = JsonDocument.Parse("""
            {"Path":"tool.exe","Arguments":"--name \"a b\"","WorkingDir":"%TEMP%","RunAsAdmin":true,"RunSilently":true}
            """);
        var info = PluginActionExecutor.BuildCustomCommandStartInfo(doc.RootElement, (configured, current, _) =>
        {
            Assert.AreEqual("%TEMP%", configured);
            Assert.IsFalse(current);
            return @"C:\expanded";
        });
        Assert.AreEqual("tool.exe", info.FileName);
        Assert.AreEqual("--name \"a b\"", info.Arguments);
        Assert.AreEqual(@"C:\expanded", info.WorkingDirectory);
        Assert.AreEqual("runas", info.Verb);
        Assert.AreEqual(ProcessWindowStyle.Hidden, info.WindowStyle);
        Assert.IsTrue(info.UseShellExecute);
    }

    [TestMethod]
    [DataRow(@"D:\literal %TEMP%", false)]
    [DataRow("", true)]
    public void BuildCustomCommandStartInfo_CurrentDirectoryIsRequiredAndLiteral(string directory, bool missing)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            Path = "tool.exe", Arguments = "", WorkingDir = directory,
            RunAsAdmin = false, RunSilently = false, UseCurrentDirectory = true
        }));
        string? Resolve(string? configured, bool current, string? context)
        {
            Assert.IsTrue(current);
            Assert.AreEqual(directory, context);
            if (missing) throw new DirectoryNotFoundException();
            return context;
        }
        if (missing)
            Assert.ThrowsExactly<DirectoryNotFoundException>(() => PluginActionExecutor.BuildCustomCommandStartInfo(doc.RootElement, Resolve));
        else
            Assert.AreEqual(directory, PluginActionExecutor.BuildCustomCommandStartInfo(doc.RootElement, Resolve).WorkingDirectory);
    }

    private sealed class LegacyProvider : IInstantResultProvider
    {
        public int Calls { get; private set; }
        public IEnumerable<InstantResultItem> GetInstantResults(string query)
        {
            Calls++;
            return [new() { Title = query, IconData = "path:unused" }];
        }
    }

    private sealed class ContextProvider : IInstantResultProvider
    {
        public bool SupportsInlineSearch => true;
        public string? ExecutedDirectory { get; private set; }
        public IEnumerable<InstantResultItem> GetInstantResults(string query) => throw new AssertFailedException("Context overload required");
        public IEnumerable<InstantResultItem> GetInstantResults(string query, string? contextDirectory) =>
            [new() { Title = query, IconData = "path:unused", OnExecute = () => ExecutedDirectory = contextDirectory }];
    }
}
