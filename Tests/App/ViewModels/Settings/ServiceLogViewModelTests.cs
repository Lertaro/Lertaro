using Lertaro.App.ViewModels.Settings;
using Lertaro.Core.Services.Search;

namespace Lertaro.App.Tests.ViewModels.Settings;

// A log file can only be truncated by the process that owns its write handle, so the clear button is only
// honest for a tab whose owner is reachable. The alternative -- enabled always, failure hidden -- is what
// made the App and Hook tabs silently do nothing.
[TestClass]
public sealed class ServiceLogViewModelTests
{
    private ServiceLogViewModel _vm = null!;

    [TestInitialize]
    public void SetUp() => _vm = new ServiceLogViewModel(new SearchService());

    [TestCleanup]
    public void TearDown() => _vm.Dispose();

    [TestMethod]
    public void ClearCommand_AppTab_IsAlwaysEnabled()
    {
        _vm.SelectedTab = "App";

        Assert.IsTrue(_vm.ClearCommand.CanExecute(null));
    }

    [TestMethod]
    public void ClearCommand_ServiceTab_FollowsServiceReadiness()
    {
        _vm.SelectedTab = "Service";
        _vm.IsServiceReady = false;
        Assert.IsFalse(_vm.ClearCommand.CanExecute(null),
            "the clear is a pipe round trip, so an unreachable service cannot answer it");

        _vm.IsServiceReady = true;
        Assert.IsTrue(_vm.ClearCommand.CanExecute(null));
    }

    [TestMethod]
    public void ClearCommand_HookTab_FollowsHookReadiness()
    {
        _vm.SelectedTab = "Hook";
        _vm.IsHookReady = false;
        Assert.IsFalse(_vm.ClearCommand.CanExecute(null),
            "hook.log belongs to the hook process, so a disconnected hook means the request never lands");

        _vm.IsHookReady = true;
        Assert.IsTrue(_vm.ClearCommand.CanExecute(null));
    }
}
