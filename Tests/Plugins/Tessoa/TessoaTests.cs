using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace Lertaro.Plugins.Tessoa.Tests;

[TestClass]
public sealed class TessoaTests
{
    [TestMethod]
    public void EveryShippedCultureResolvesThePluginAndSettingsLabels()
    {
        var plugin = new TessoaPlugin();
        foreach (var culture in new[] { "en-US", "es-ES", "ja-JP", "ko-KR", "zh-CN", "zh-HK", "zh-TW" })
        {
            var strings = plugin.GetTranslations(culture);
            foreach (var key in plugin.GetConfigSchema().Fields.Select(field => field.LabelKey)
                .Concat(["Tessoa_Description", "Tessoa_Open", "Tessoa_Failed"]))
                Assert.IsFalse(string.IsNullOrWhiteSpace(strings.GetValueOrDefault(key)), $"{culture}: {key}");
        }
    }

    [TestMethod]
    public void TabListKeepsSpacesUncPathsAndWindowOrderAndIgnoresMalformedRows()
    {
        const string reply = "api 3\ntab 17 c D:\\My Work\ntab 23 v \\\\server\\share\ntab 31 b D:\\资料\ntab 32 x C:\\wrong\ntab ? b C:\\bad\ntab 5 c relative\n";
        CollectionAssert.AreEqual(new[] { @"D:\My Work", @"\\server\share", @"D:\资料" }, TessoaQuery.TabPaths(reply).ToArray());
        Assert.IsTrue(TessoaQuery.SupportsTabs("api 2\n"));
        Assert.IsFalse(TessoaQuery.SupportsTabs(""));
        Assert.IsFalse(TessoaQuery.SupportsTabs("api 1\n"));
        Assert.IsFalse(TessoaQuery.SupportsTabs("api 2x\n"));
        Assert.IsEmpty(TessoaQuery.TabPaths("tab 17 c C:\\wrong"));
    }

    [TestMethod]
    [DataRow(0, true, 1)]
    [DataRow(1, true, 2)]
    [DataRow(2, false, 1)]
    [DataRow(-1, false, 1)]
    public void ScopedNavigationRetriesOnlyVersionRejection(int exit, bool expected, int attempts)
    {
        var calls = new List<string[]>();
        var result = TessoaNavigation.Send("tessoa.exe", @"D:\My Work\资料.txt", true, 42, "api 2\n", info =>
        {
            Assert.IsFalse(info.UseShellExecute);
            calls.Add(info.ArgumentList.ToArray());
            return calls.Count == 1 ? exit : 0;
        });
        Assert.AreEqual(expected, result);
        Assert.HasCount(attempts, calls);
        CollectionAssert.AreEqual(new[] { @"--reveal-window=D:\My Work\资料.txt", "--in-tab=42" }, calls[0]);
        if (attempts == 2) CollectionAssert.AreEqual(new[] { calls[0][0] }, calls[1]);
    }

    [TestMethod]
    [DataRow(0, null)]
    [DataRow(42, "")]
    public void UntargetedFolderRequestKeepsTheRootAndDoesNotRetry(int window, string? tabs)
    {
        var calls = 0;
        Assert.IsFalse(TessoaNavigation.Send("tessoa.exe", @"C:\", false, window, tabs, info =>
        {
            calls++;
            CollectionAssert.AreEqual(new[] { @"--open-window=C:\" }, info.ArgumentList.ToArray());
            return 1;
        }));
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("unexpected response")]
    public void MissingOrInvalidWindowReplyDoesNotLaunch(string? tabs)
    {
        Assert.IsFalse(TessoaNavigation.Send("tessoa.exe", @"C:\", false, 42, tabs,
            _ => throw new AssertFailedException("An unconfirmed window must not trigger program-wide navigation.")));
    }

    [TestMethod]
    public void ExplicitMissingPathNeverFallsBackToDiscovery()
    {
        Assert.IsNull(TessoaNavigation.Resolve(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "tessoa.exe"), null));
        Assert.IsFalse(new TessoaAdapter().CanTrigger(123, ""));
    }

    [TestMethod]
    [DataRow("path")]
    [DataRow("empty")]
    [DataRow("oversize")]
    [DataRow("negative")]
    [DataRow("short")]
    [DataRow("utf8")]
    public async Task PipeReadsOneBoundedFrameForTheExactWindow(string scenario)
    {
        var name = "lertaro-tessoa-test-" + Guid.NewGuid().ToString("N");
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var accepted = server.WaitForConnectionAsync(limit.Token);
        var client = TessoaQuery.QueryAsync(name, 123, "query-cwd");
        await accepted;
        var prefix = new byte[4];
        await server.ReadExactlyAsync(prefix, limit.Token);
        var expected = Encoding.UTF8.GetBytes("v 1\nop query-cwd\nhwnd 123\n");
        Assert.AreEqual(expected.Length, BinaryPrimitives.ReadInt32LittleEndian(prefix));
        var request = new byte[expected.Length];
        await server.ReadExactlyAsync(request, limit.Token);
        CollectionAssert.AreEqual(expected, request);
        const string folder = @"D:\测试 folder";
        byte[] body = scenario switch { "path" => Encoding.UTF8.GetBytes(folder), "utf8" => [0xff], _ => [] };
        BinaryPrimitives.WriteInt32LittleEndian(prefix, scenario switch { "oversize" => int.MaxValue, "negative" => -1, "short" => 7, _ => body.Length });
        await server.WriteAsync(prefix.AsMemory(0, 1), limit.Token);
        await server.WriteAsync(prefix.AsMemory(1), limit.Token);
        if (body.Length != 0) await server.WriteAsync(body, limit.Token);
        if (scenario == "short") server.Dispose();
        Assert.AreEqual(scenario switch { "path" => folder, "empty" => "", _ => null }, await client);
    }

    [TestMethod, TestCategory("TessoaIntegration")]
    public void InstalledTessoaReportsTheDocumentedFileApi()
    {
        var configured = Environment.GetEnvironmentVariable("TESSOA_TEST_EXE");
        var observed = TessoaWindow.Visible().FirstOrDefault()?.Image;
        if (!string.IsNullOrEmpty(configured) && !File.Exists(configured)) Assert.Inconclusive("Configured tessoa.exe is not installed.");
        var executable = TessoaNavigation.Resolve(configured, observed);
        if (executable == null && configured == null) Assert.Inconclusive("No compatible tessoa installation.");
        Assert.IsNotNull(executable, "The configured executable must advertise tessoa-file-app-api=1.");
    }

    [TestMethod, TestCategory("TessoaIntegration")]
    public void RunningWindowAnswersReadOnlyQueriesWithoutChangingTabs()
    {
        var configured = Environment.GetEnvironmentVariable("TESSOA_TEST_EXE");
        var window = TessoaWindow.Visible().FirstOrDefault(candidate => string.IsNullOrEmpty(configured)
            || string.Equals(candidate.Image, configured, StringComparison.OrdinalIgnoreCase));
        if (window == null) Assert.Inconclusive("No running window for this tessoa executable.");
        var cwd = TessoaQuery.Read(window, "query-cwd");
        Assert.IsNotNull(cwd, "The window did not answer query-cwd.");
        Assert.IsTrue(cwd.Length == 0 || TessoaQuery.IsPath(cwd));
        var tabs = TessoaQuery.Read(window, "query-tabs");
        Assert.IsNotNull(tabs, "The window did not answer query-tabs.");
        Assert.IsTrue(tabs.Length == 0 || TessoaQuery.SupportsTabs(tabs));
        Assert.AreEqual("", TessoaQuery.Read(window with { Handle = -1 }, "query-cwd"), "A foreign HWND must not return another window's path.");
    }
}
