using System.Net;
using System.Net.Http.Headers;
using System.Collections.Concurrent;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>Retains the receiver-issued upload tokens until a transient upload failure is retried.</summary>
internal sealed class LocalSendPendingFileTransfer
{
    internal required string TargetIp { get; init; }
    internal required int TargetPort { get; init; }
    internal required bool Https { get; init; }
    internal required string? SessionId { get; init; }
    internal required string? TargetVersion { get; init; }
    internal required IReadOnlyList<LocalSendPendingFile> Files { get; init; }
    internal required IReadOnlyDictionary<string, string> Tokens { get; init; }
    private readonly ConcurrentDictionary<string, byte> _failedFileIds = new();

    internal bool HasFailedFiles => !_failedFileIds.IsEmpty;
    internal IReadOnlyList<LocalSendPendingFile> GetFilesForAttempt() => HasFailedFiles
        ? Files.Where(file => _failedFileIds.ContainsKey(file.Id)).ToArray()
        : Files.Where(file => Tokens.ContainsKey(file.Id)).ToArray();
    internal void MarkFileSucceeded(string fileId) => _failedFileIds.TryRemove(fileId, out _);
    internal void MarkFileFailed(string fileId) => _failedFileIds.TryAdd(fileId, 0);
}

internal sealed record LocalSendPendingFile(string Id, LocalSendFileDto File, string Path);

internal sealed record LocalSendFileTransferAttempt(LocalSendSendResult Result, string? Error, bool CanRetry);

/// <summary>
/// Split out to keep LocalSendClient under the repository file limit. It always operates on one retained transfer.
/// </summary>
internal static class LocalSendFileTransferSender
{
    private const int MaxChecksumAttempts = 3;

    internal static async Task<LocalSendFileTransferAttempt> UploadWithSenderCancellationAsync(
        Func<CancellationToken, Task<LocalSendFileTransferAttempt>> upload,
        Func<Task> notifyReceiver,
        CancellationToken userToken)
    {
        if (!userToken.CanBeCanceled)
            return await upload(userToken).ConfigureAwait(false);

        using var uploadCancellation = new CancellationTokenSource();
        var userCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = userToken.Register(userCanceled.SetResult);
        var uploadTask = upload(uploadCancellation.Token);

        await Task.WhenAny(uploadTask, userCanceled.Task).ConfigureAwait(false);
        if (userCanceled.Task.IsCompleted)
        {
            // Notify the receiver before aborting the request body; otherwise it can report the truncated upload as an error.
            await notifyReceiver().ConfigureAwait(false);
            uploadCancellation.Cancel();
        }

        return await uploadTask.ConfigureAwait(false);
    }

    internal static async Task<LocalSendFileTransferAttempt> UploadAsync(HttpClient client, LocalSendServer? server,
        LocalSendPendingFileTransfer transfer, Action<LocalSendSendProgressArgs>? onProgress,
        Action<LocalSendFileConfirmationArgs>? onFileConfirmed, CancellationToken token)
    {
        using var outgoing = server?.StartOutgoingSession(transfer.TargetIp, transfer.SessionId, LocalSendApiRoute.UsesV1(transfer.TargetVersion));
        using var linked = outgoing == null ? null : CancellationTokenSource.CreateLinkedTokenSource(token, outgoing.Cancellation.Token);
        var transferToken = linked?.Token ?? token;

        var files = transfer.GetFilesForAttempt();
        var attempts = new ConcurrentBag<(LocalSendPendingFile File, LocalSendFileTransferAttempt Attempt)>();
        var next = -1;
        var workers = Enumerable.Range(0, Math.Min(2, files.Count)).Select(async _ =>
        {
            if (transferToken.IsCancellationRequested)
                return;
            while (Interlocked.Increment(ref next) is var index && index < files.Count)
            {
                var pendingFile = files[index];
                var attempt = await UploadFileAsync(client, transfer, pendingFile, index + 1, files.Count, onProgress, transferToken, token).ConfigureAwait(false);
                onFileConfirmed?.Invoke(new LocalSendFileConfirmationArgs(pendingFile.Id, pendingFile.File.FileName, index + 1, files.Count, attempt.Result, attempt.Error));
                if (attempt.Result == LocalSendSendResult.Success)
                {
                    transfer.MarkFileSucceeded(pendingFile.Id);
                }
                else if (attempt.CanRetry)
                    transfer.MarkFileFailed(pendingFile.Id);
                attempts.Add((pendingFile, attempt));
                if (transferToken.IsCancellationRequested)
                    return;
            }
        });
        await Task.WhenAll(workers).ConfigureAwait(false);

        if (transferToken.IsCancellationRequested)
            return new LocalSendFileTransferAttempt(GetCancellationResult(token), null, false);
        var terminalFailure = attempts.Select(item => item.Attempt).FirstOrDefault(attempt => attempt.Result != LocalSendSendResult.Success && !attempt.CanRetry);
        if (terminalFailure != null)
            return terminalFailure;
        if (transfer.HasFailedFiles)
            return attempts.First(item => item.Attempt.CanRetry).Attempt;
        return new LocalSendFileTransferAttempt(LocalSendSendResult.Success, null, false);
    }

    private static async Task<LocalSendFileTransferAttempt> UploadFileAsync(HttpClient client, LocalSendPendingFileTransfer transfer,
        LocalSendPendingFile pendingFile, int fileIndex, int totalFiles, Action<LocalSendSendProgressArgs>? onProgress,
        CancellationToken token, CancellationToken userToken)
    {
        if (!transfer.Tokens.TryGetValue(pendingFile.Id, out var fileToken))
            return new LocalSendFileTransferAttempt(LocalSendSendResult.Success, null, false);
        var sessionQuery = LocalSendApiRoute.UsesV1(transfer.TargetVersion) ? string.Empty : $"sessionId={Uri.EscapeDataString(transfer.SessionId!)}&";
        var url = LocalSendApiRoute.BuildUri(transfer.TargetIp, transfer.TargetPort, transfer.Https, "upload", transfer.TargetVersion) +
            $"?{sessionQuery}fileId={Uri.EscapeDataString(pendingFile.Id)}&token={Uri.EscapeDataString(fileToken)}";
        for (var attemptNumber = 1; attemptNumber <= MaxChecksumAttempts; attemptNumber++)
        {
            try
            {
                using var file = File.OpenRead(pendingFile.Path);
                onProgress?.Invoke(new LocalSendSendProgressArgs(pendingFile.File.FileName, 0, file.Length,
                    fileIndex, totalFiles));
                using var content = new ProgressiveStreamContent(file, (sent, total) => onProgress?.Invoke(
                    new LocalSendSendProgressArgs(pendingFile.File.FileName, sent, total, fileIndex, totalFiles)));
                content.Headers.ContentType = new MediaTypeHeaderValue(LocalSendClientHelper.GetMimeTypeForFileName(pendingFile.File.FileName));
                using var response = await client.PostAsync(url, content, token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return new LocalSendFileTransferAttempt(LocalSendSendResult.Success, null, false);
                if ((int)response.StatusCode == 422 && attemptNumber < MaxChecksumAttempts)
                    continue;

                var attempt = ClassifyFailure(response.StatusCode, response.ReasonPhrase);
                Logger.Log($"[LocalSendClient] Upload failed for {pendingFile.File.FileName}: {attempt.Error}", LogLevel.Error);
                return attempt;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return new LocalSendFileTransferAttempt(GetCancellationResult(userToken), null, false);
            }
            catch (OperationCanceledException)
            {
                return new LocalSendFileTransferAttempt(LocalSendSendResult.ReceiverCanceled, null, false);
            }
            catch (Exception ex)
            {
                Logger.Log($"[LocalSendClient] Transfer interrupted for {pendingFile.File.FileName}: {ex.GetType().Name} - {ex.Message}", LogLevel.Warn);
                return new LocalSendFileTransferAttempt(LocalSendSendResult.Error, ex.Message, true);
            }
        }

        return new LocalSendFileTransferAttempt(LocalSendSendResult.Error, "Checksum mismatch", false);
    }

    internal static LocalSendSendResult GetCancellationResult(CancellationToken userToken) =>
        userToken.IsCancellationRequested ? LocalSendSendResult.Canceled : LocalSendSendResult.ReceiverCanceled;

    internal static LocalSendFileTransferAttempt ClassifyFailure(HttpStatusCode statusCode, string? reasonPhrase) => statusCode switch
    {
        _ when (int)statusCode == 422 => new(LocalSendSendResult.Error, "HTTP 422 Checksum mismatch", false),
        _ when (int)statusCode >= 500 => new(LocalSendSendResult.RemoteError, $"HTTP {(int)statusCode} {reasonPhrase}", true),
        _ => new(LocalSendSendResult.Error, $"HTTP {(int)statusCode} {reasonPhrase}", true)
    };
}
