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
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void HandleCanceled(object? sender, string canceledSessionId)
        {
            if (string.IsNullOrEmpty(canceledSessionId) || canceledSessionId == sessionId)
                canceled.TrySetResult();
        }

        server.SessionCanceled += HandleCanceled;
        LocalSendUploadRequestArgs? args = null;
        args = new LocalSendUploadRequestArgs(sessionId, dto, accept => tcs.TrySetResult((accept, args?.CustomDownloadDirectory, args?.SelectedFileIds)), isAutoAccepted);
        try
        {
            server.InvokeUploadRequested(args);
            if (isAutoAccepted)
                tcs.TrySetResult((true, null, null));
            if (server.IsSessionCanceled(sessionId))
                return (false, null, null);

            var completed = await Task.WhenAny(tcs.Task, canceled.Task).ConfigureAwait(false);
            return completed == tcs.Task ? await tcs.Task.ConfigureAwait(false) : (false, null, null);
        }
        finally
        {
            server.SessionCanceled -= HandleCanceled;
        }
    }
}
