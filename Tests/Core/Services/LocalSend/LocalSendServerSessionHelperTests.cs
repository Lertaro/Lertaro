using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendServerSessionHelperTests
{
    [TestMethod]
    public async Task RequestAcceptanceAsync_NoHandler_ReturnsFalse()
    {
        var server = new LocalSendServer();
        var dto = new PrepareUploadRequestDto
        {
            Info = new LocalSendDeviceInfo { Alias = "TestDevice" },
            Files = new Dictionary<string, LocalSendFileDto>()
        };

        var res = await LocalSendServerSessionHelper.RequestAcceptanceAsync(server, "s1", dto);
        Assert.IsFalse(res.Accepted);
        Assert.IsNull(res.CustomDir);
        Assert.IsNull(res.SelectedFileIds);
    }

    [TestMethod]
    public async Task RequestAcceptanceAsync_WaitsForTheUserResponse()
    {
        var server = new LocalSendServer();
        var responseReady = new TaskCompletionSource<LocalSendUploadRequestArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.UploadRequested += (_, args) => responseReady.TrySetResult(args);
        var dto = new PrepareUploadRequestDto
        {
            Info = new LocalSendDeviceInfo { Alias = "TestDevice" },
            Files = new Dictionary<string, LocalSendFileDto>()
        };

        var pending = LocalSendServerSessionHelper.RequestAcceptanceAsync(server, "s1", dto);
        var args = await responseReady.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsFalse(pending.IsCompleted);
        args.Respond(true);

        var response = await pending;
        Assert.IsTrue(response.Accepted);
    }

    [TestMethod]
    public async Task RequestAcceptanceAsync_CancelUnblocksPendingRequest()
    {
        var server = new LocalSendServer();
        var requestReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.UploadRequested += (_, _) => requestReady.TrySetResult();
        var dto = new PrepareUploadRequestDto
        {
            Info = new LocalSendDeviceInfo { Alias = "TestDevice" },
            Files = new Dictionary<string, LocalSendFileDto>()
        };

        var pending = LocalSendServerSessionHelper.RequestAcceptanceAsync(server, "s1", dto);
        await requestReady.Task.WaitAsync(TimeSpan.FromSeconds(1));
        server.CancelSession("s1");

        var response = await pending.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.IsFalse(response.Accepted);
    }
}
