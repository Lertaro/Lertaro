using Flow.Launcher.Plugin;
using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
public sealed class FlowResultMapperTests
{
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
