using Flow.Launcher.Plugin;
using System.Windows.Controls;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
[DoNotParallelize]
public sealed class FlowResultMapperTests
{
    [StaTestMethod]
    [DataRow(99)]
    [DataRow(150)]
    public void RepeatedMaps_KeepLiveDictionaryPreviewsAfterCacheChurn(int count)
    {
        var oldDirectory = UserDataService.GetUserDataDirectoryFunc;
        var directory = Directory.CreateTempSubdirectory("FlowPreviewLifetime-");
        UserDataService.GetUserDataDirectoryFunc = () => directory.FullName;
        try
        {
            var results = Enumerable.Range(0, count).Select(i => new Result
            {
                Title = $"word-{i}",
                PreviewPanel = new Lazy<UserControl>(() => new UserControl { Content = new TextBlock { Text = $"definition-{i}" } })
            }).ToList();
            var first = FlowResultMapper.MapToInstantResults(results);
            for (var repeat = 0; repeat < 8; repeat++)
            {
                var next = FlowResultMapper.MapToInstantResults(results);
                CollectionAssert.AreEqual(first.Select(r => r.ActionArgument).ToArray(), next.Select(r => r.ActionArgument).ToArray());
            }
            for (var i = 0; i < 200; i++)
                PluginPreviewCache.Register("other", "other", new Lazy<UserControl>(() => new UserControl()));
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            foreach (var (item, i) in first.Select((item, i) => (item, i)))
            {
                var panel = Assert.IsInstanceOfType<UserControl>(PluginPreviewCache.GetPreview(item.ActionArgument));
                Assert.AreEqual($"definition-{i}", Assert.IsInstanceOfType<TextBlock>(panel.Content).Text);
            }
            GC.KeepAlive(first);
        }
        finally
        {
            UserDataService.GetUserDataDirectoryFunc = oldDirectory;
            directory.Delete(true);
        }
    }

    [TestMethod]
    public void MapToInstantResult_MapsBasicProperties()
    {
        var result = new Result
        {
            Title = "Calculated Value",
            SubTitle = "42",
            AutoCompleteText = "42",
            CopyText = "42",
            Score = 100
        };

        var mapped = FlowResultMapper.MapToInstantResult(result);

        Assert.AreEqual("Calculated Value", mapped.Title);
        Assert.AreEqual("42", mapped.Description);
        Assert.AreEqual("42", mapped.TabCompletion);
        Assert.AreEqual("42", mapped.ActionArgument);
        Assert.AreEqual("Execute", mapped.ActionType);
        Assert.IsNotNull(mapped.OnExecute);
    }

    [TestMethod]
    public void MapToInstantResult_ExecutesActionCallback()
    {
        var actionExecuted = false;
        var result = new Result
        {
            Title = "Action Test",
            Action = _ =>
            {
                actionExecuted = true;
                return true;
            }
        };

        var mapped = FlowResultMapper.MapToInstantResult(result);
        mapped.OnExecute?.Invoke();

        Assert.IsTrue(actionExecuted);
    }

    [TestMethod]
    public void MapToInstantResult_ActionReturningFalse_ReturnsFalse()
    {
        var result = new Result
        {
            Title = "Stay Open Action",
            Action = _ => false
        };

        var mapped = FlowResultMapper.MapToInstantResult(result);
        var shouldHide = mapped.OnExecuteFunc?.Invoke();

        Assert.IsFalse(shouldHide);
    }

    [TestMethod]
    public void MapToInstantResult_AutoCompleteTextOnly_ChangesQueryAndReturnsFalse()
    {
        string? changedQuery = null;
        bool? requeryFlag = null;
        PluginSdk.Services.SearchQueryService.ChangeQueryFunc = (q, r) =>
        {
            changedQuery = q;
            requeryFlag = r;
        };

        try
        {
            var result = new Result
            {
                Title = "AutoComplete Prompt",
                AutoCompleteText = "qr mytext"
            };

            var mapped = FlowResultMapper.MapToInstantResult(result);
            var shouldHide = mapped.OnExecuteFunc?.Invoke();

            Assert.IsFalse(shouldHide);
            Assert.AreEqual("qr mytext", changedQuery);
            Assert.IsTrue(requeryFlag);
        }
        finally
        {
            PluginSdk.Services.SearchQueryService.ChangeQueryFunc = null;
        }
    }

    [TestMethod]
    public void MapToInstantResult_AsyncActionReturningFalse_ReturnsFalse()
    {
        var result = new Result
        {
            Title = "Async Stay Open Action",
            AsyncAction = _ => ValueTask.FromResult(false)
        };

        var mapped = FlowResultMapper.MapToInstantResult(result);
        var shouldHide = mapped.OnExecuteFunc?.Invoke();

        Assert.IsFalse(shouldHide);
    }
}
