using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>
/// Session user acceptance helper for LocalSendServer.
/// ponytail: Split out purely to keep LocalSendServer.cs under the repo's 300-line limit.
/// </summary>
public static class LocalSendServerSessionHelper
{
    public static async Task<(bool Accepted, string? CustomDir, HashSet<string>? SelectedFileIds)> RequestAcceptanceAsync(
        LocalSendServer server, string sessionId, PrepareUploadRequestDto dto, bool isAutoAccepted = false)
    {
        if (!server.HasUploadRequestedHandler) return (isAutoAccepted, null, null);

        var tcs = new TaskCompletionSource<(bool, string?, HashSet<string>?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        LocalSendUploadRequestArgs? args = null;
        args = new LocalSendUploadRequestArgs(sessionId, dto, accept => tcs.TrySetResult((accept, args?.CustomDownloadDirectory, args?.SelectedFileIds)), isAutoAccepted);

        server.InvokeUploadRequested(args);
        if (isAutoAccepted)
        {
            tcs.TrySetResult((true, null, null));
        }

        return await tcs.Task.ConfigureAwait(false);
    }
}
