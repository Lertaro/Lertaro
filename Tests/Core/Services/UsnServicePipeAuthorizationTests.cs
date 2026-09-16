using Lertaro.Core.Services;
using Lertaro.Core.Wire;

namespace Lertaro.Core.Tests.Services;

// The service pipe is reachable by any process that can open it, so every command that changes service
// state or destroys data has to be gated on the caller being this install's own App. The gate is
// table-driven at the dispatch site rather than repeated per case, because per-case guards are exactly
// how Initialize, CancelDriveIndex, ClearServiceLog and ClearPathCaches came to ship unauthenticated.
[TestClass]
public sealed class UsnServicePipeAuthorizationTests
{
    private static readonly SearchRequestId[] ControlCommands =
    [
        SearchRequestId.Rebuild,
        SearchRequestId.Initialize,
        SearchRequestId.RebuildDrive,
        SearchRequestId.DeleteDriveIndex,
        SearchRequestId.CancelDriveIndex,
        SearchRequestId.SetMachineSettings,
        SearchRequestId.ClearServiceLog,
        SearchRequestId.ClearPathCaches,
    ];

    // Read/query commands, plus the streaming ones the server handles before dispatch. None of them may
    // be gated here: they are how any client finds out whether the service is up, and gating them would
    // cut the CLI and the installer off from the service entirely.
    private static readonly SearchRequestId[] OpenCommands =
    [
        SearchRequestId.Ping,
        SearchRequestId.Status,
        SearchRequestId.GetMachineSettings,
        SearchRequestId.GetFileMetadata,
        SearchRequestId.GetRecentFiles,
        SearchRequestId.GetSpaceEntries,
        SearchRequestId.Search,
        SearchRequestId.SearchDir,
        SearchRequestId.EnumerateDir,
        SearchRequestId.SubscribeStatus,
        SearchRequestId.SubscribeDirectoryChanges,
        SearchRequestId.LaunchHook,
    ];

    [TestMethod]
    public void RequiresAuthorizedCaller_GatesEveryStateChangingCommand()
    {
        foreach (var id in ControlCommands)
            Assert.IsTrue(UsnServicePipeRequestProcessor.RequiresAuthorizedCaller(id), id.ToString());
    }

    [TestMethod]
    public void RequiresAuthorizedCaller_LeavesReadAndQueryCommandsOpen()
    {
        foreach (var id in OpenCommands)
            Assert.IsFalse(UsnServicePipeRequestProcessor.RequiresAuthorizedCaller(id), id.ToString());
    }

    [TestMethod]
    public void EveryCommandIsClassifiedAsControlOrOpen()
    {
        // The switch's default arm is "open", so a newly added state-changing command would ship
        // unauthenticated unless someone classifies it. Failing here is that reminder.
        var classified = ControlCommands.Concat(OpenCommands).ToHashSet();
        var unclassified = Enum.GetValues<SearchRequestId>().Where(id => !classified.Contains(id)).ToList();

        Assert.HasCount(0, unclassified, $"Unclassified commands: {string.Join(", ", unclassified)}");
    }
}
