using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>
/// LocalSend HTTP server backed by a raw TcpListener so it works without
/// Windows URL ACL reservations or administrator privileges.
/// Handles only the LocalSend v1/v2 API surface we need.
/// </summary>
public sealed class LocalSendServer : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private readonly LocalSendReceiveSessionStore _receiveSessions = new();
    private readonly LocalSendOutgoingSessionStore _outgoingSessions = new();

    public LocalSendDeviceInfo DeviceInfo { get; set; } = new()
    {
        Alias = Environment.MachineName,
        DeviceModel = "Windows",
        DeviceType = "desktop",
        Port = 53317,
        Protocol = "http"
    };
    public string DownloadDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public bool QuickSave { get; set; } = false;
    public bool VerifyChecksums { get; set; } = true;
    public string? ReceivePin { get; set; }
    public string ShowToken { get; set; } = string.Empty;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _pinAttempts = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _pinAttemptTimes = new();
    internal bool CheckPin(string clientIp, string? requestPin, out int statusCode, out string? jsonBody)
        => LocalSendServerHelper.CheckPin(ReceivePin, _pinAttempts, clientIp, requestPin, out statusCode, out jsonBody, _pinAttemptTimes);

    public event EventHandler<LocalSendUploadRequestArgs>? UploadRequested;
    public event EventHandler<(string FileId, string Path)>? FileReceived;
    public event EventHandler<LocalSendDeviceInfo>? DeviceRegistered;
    public event EventHandler<IReadOnlyList<string>?>? ShowRequested;

    public int ActualPort { get; private set; }
    public X509Certificate2? Certificate { get; set; }
    internal X509Certificate2? IdentityCertificate { get; set; }
    public bool IsBusy => LocalSendServiceManager.Instance.IsWindowOpen || _receiveSessions.HasSessions;

    public void Start(int port = 53317)
    {
        if (_listener != null) return;
        _cts = new CancellationTokenSource();
        for (var p = port; p < port + 10; p++)
        {
            try
            {
                var l = LocalSendServerHelper.TryCreateDualStackListener(p) ?? new TcpListener(IPAddress.Any, p);
                l.Start();
                _listener = l;
                ActualPort = p;
                DeviceInfo.Port = p;
                break;
            }
            catch { }
        }
        if (_listener == null) throw new InvalidOperationException("Failed to bind LocalSend port.");
        _listenTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(token).ConfigureAwait(false);
                client.NoDelay = true;
                _ = Task.Run(() => HandleClientAsync(client, token), token);
            }
            catch (OperationCanceledException) { break; }
            catch { await Task.Delay(200, token).ConfigureAwait(false); }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                // ponytail: no receive timeout — transfers can be arbitrarily slow; cancellation is via _cts.
                var connection = await LocalSendTlsHelper.CreateServerStreamAsync(client, Certificate, token).ConfigureAwait(false);
                using var stream = connection.Stream;
                await LocalSendServerHandler.ProcessAsync(
                    this, stream, client.Client.RemoteEndPoint, connection.PeerFingerprint, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Log($"[LocalSendServer] Client handling error: {ex.Message}", LogLevel.Debug);
            }
        }
    }

    internal bool TryRegisterActiveSession(string sessionId, PrepareUploadRequestDto dto)
        => _receiveSessions.TryRegister(sessionId, dto, LocalSendServiceManager.Instance.IsWindowOpen);
    public event EventHandler<LocalSendProgressArgs>? ProgressChanged;
    public event EventHandler<string>? SessionCanceled;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<string, byte>> _sessionCompletedFiles = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<string, long>> _sessionTransferredBytes = new();

    public void CancelSession(string sessionId, bool notifySender = false)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        var (canceled, prepareDto) = _receiveSessions.Cancel(sessionId);
        if (!canceled || prepareDto == null)
            return;
        if (notifySender)
            _ = LocalSendServerHelper.NotifySenderCanceledAsync(prepareDto.Info, sessionId);
        SessionCanceled?.Invoke(this, sessionId);
    }

    public void CancelAllSessions()
    {
        var activeIds = _receiveSessions.GetAll().Select(session => session.Key).ToList();
        if (activeIds.Count == 0)
        {
            SessionCanceled?.Invoke(this, string.Empty);
        }
        else
        {
            foreach (var id in activeIds)
            {
                CancelSession(id);
            }
        }
    }

    public void UnregisterSession(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        _receiveSessions.Unregister(sessionId);
        _sessionCompletedFiles.TryRemove(sessionId, out _);
        _sessionTransferredBytes.TryRemove(sessionId, out _);
    }

    public bool IsSessionCanceled(string sessionId) =>
        _receiveSessions.IsCanceled(sessionId);

    internal bool HasActiveSessions => _receiveSessions.HasSessions;
    internal bool HasUploadAuthorization(string sessionId) => _receiveSessions.HasAuthorization(sessionId);
    internal bool TryGetActiveSession(string sessionId, out PrepareUploadRequestDto? dto) => _receiveSessions.TryGet(sessionId, out dto);
    internal KeyValuePair<string, PrepareUploadRequestDto>[] GetActiveSessions() => _receiveSessions.GetAll();
    internal LocalSendOutgoingSession StartOutgoingSession(string remoteIp, string? remoteSessionId, bool legacy) => _outgoingSessions.Start(remoteIp, remoteSessionId, legacy);
    internal bool TryCancelOutgoingSession(string? remoteSessionId, string senderIp, bool v2) => _outgoingSessions.TryCancel(remoteSessionId, senderIp, v2);

    internal async Task HandleUploadAsync(
        Stream stream, Stream requestBody, string sessionId, string fileId, string token, string senderIp, bool v2)
    {
        if (!v2 && !HasActiveSessions)
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, 409, "{\"message\":\"No session\"}").ConfigureAwait(false);
            return;
        }
        if (!TryStartUpload(sessionId, fileId, token, senderIp, v2, out var context, out var authorizationError))
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, 403, $"{{\"message\":\"{authorizationError}\"}}").ConfigureAwait(false);
            return;
        }
        sessionId = context.SessionId;
        var prepareDto = context.Request;
        var fileName = context.File.FileName;
        var senderAlias = prepareDto.Info.Alias;
        var metadata = context.File.Metadata;
        var expectedSha256 = VerifyChecksums ? context.File.Sha256 ?? context.File.Hash : null;
        var totalBytes = context.File.Size;
        var keys = prepareDto.Files.Keys.ToList();
        var fileIndex = Math.Max(1, keys.IndexOf(fileId) + 1);
        var totalFiles = prepareDto.Files.Count;
        var selectedIds = context.SelectedFileIds;
        var expectedTotalFiles = selectedIds?.Count ?? totalFiles;
        var targetPath = LocalSendServerHelper.ResolveTargetPath(context.DownloadDirectory, fileName);
        if (targetPath == null)
        {
            var sessionEnded = v2 && CompleteUploadAttempt(sessionId, fileId, LocalSendFileSaveStatus.Error);
            ProgressChanged?.Invoke(this, new LocalSendProgressArgs(sessionId, senderAlias, fileId, fileName, 0, totalBytes, fileIndex, expectedTotalFiles, isAllDone: sessionEnded, isFailed: true));
            if (sessionEnded) UnregisterSession(sessionId);
            await LocalSendServerHelper.WriteResponseAsync(stream, 403).ConfigureAwait(false);
            return;
        }

        long lastFlushedBytes = 0;
        long lastProgressTimeMs = 0;
        var progressStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var saveResult = await LocalSendIncomingFileWriter.SaveAsync(
            requestBody, targetPath, totalBytes, expectedSha256, () => false, bytesReadTotal =>
            {
                if (context.SessionCancellation.IsCancellationRequested)
                    return;
                if (progressStopwatch.ElapsedMilliseconds - lastProgressTimeMs >= 100 || bytesReadTotal - lastFlushedBytes >= 512 * 1024)
                {
                    lastProgressTimeMs = progressStopwatch.ElapsedMilliseconds;
                    lastFlushedBytes = bytesReadTotal;
                    var sessionTransferred = _sessionTransferredBytes.GetOrAdd(sessionId, _ => new System.Collections.Concurrent.ConcurrentDictionary<string, long>());
                    sessionTransferred[fileId] = bytesReadTotal;
                    var currentSessionTransferred = sessionTransferred.Values.Sum();
                    var currentSessionTotal = prepareDto != null ? prepareDto.Files.Where(kv => selectedIds == null || selectedIds.Contains(kv.Key)).Sum(kv => kv.Value.Size) : totalBytes;
                    ProgressChanged?.Invoke(this, new LocalSendProgressArgs(sessionId, senderAlias, fileId, fileName, bytesReadTotal, totalBytes, fileIndex, totalFiles, isFinished: false, savedPath: targetPath, sessionBytesTransferred: currentSessionTransferred, sessionTotalBytes: currentSessionTotal));
                }
            }, checksumBytes =>
            {
                if (!context.SessionCancellation.IsCancellationRequested)
                    ProgressChanged?.Invoke(this, new LocalSendProgressArgs(sessionId, senderAlias, fileId,
                        fileName, checksumBytes, totalBytes, fileIndex, expectedTotalFiles, savedPath: targetPath,
                        stage: LocalSendTransferStage.VerifyingChecksum));
            }).ConfigureAwait(false);
        var bytesReadTotal = saveResult.BytesWritten;

        if (saveResult.Status != LocalSendFileSaveStatus.Success)
        {
            var sessionEnded = !context.SessionCancellation.IsCancellationRequested && v2 && CompleteUploadAttempt(sessionId, fileId, saveResult.Status);
            LocalSendServerHelper.TryDeleteFile(targetPath);
            Logger.Log($"[LocalSendServer] Upload failed for {fileName}: {saveResult.Error ?? saveResult.Status.ToString()}", LogLevel.Warn);
            if (!context.SessionCancellation.IsCancellationRequested && saveResult.Status != LocalSendFileSaveStatus.Canceled)
                ProgressChanged?.Invoke(this, new LocalSendProgressArgs(sessionId, senderAlias, fileId, fileName, bytesReadTotal, totalBytes, fileIndex, expectedTotalFiles, isAllDone: sessionEnded, isFailed: true));
            var status = saveResult.Status == LocalSendFileSaveStatus.ChecksumMismatch ? 422 : 500;
            var message = status == 422 ? "Checksum mismatch" : "Could not save file. Check receiving device for more information.";
            if (sessionEnded || context.SessionCancellation.IsCancellationRequested) UnregisterSession(sessionId);
            await LocalSendServerHelper.WriteResponseAsync(stream, status, $"{{\"message\":\"{message}\"}}").ConfigureAwait(false);
            return;
        }

        var completedSet = _sessionCompletedFiles.GetOrAdd(sessionId, _ => new System.Collections.Concurrent.ConcurrentDictionary<string, byte>());
        completedSet[fileId] = 0;

        var isAllDone = v2 ? CompleteUploadAttempt(sessionId, fileId, LocalSendFileSaveStatus.Success) : completedSet.Count >= expectedTotalFiles;
        if (context.SessionCancellation.IsCancellationRequested)
        {
            LocalSendFileMetadataApplier.Apply(targetPath, metadata);
            FileReceived?.Invoke(this, (fileId, targetPath));
            UnregisterSession(sessionId);
            await LocalSendServerHelper.WriteResponseAsync(stream, 200).ConfigureAwait(false);
            return;
        }
        var displayIndex = isAllDone ? expectedTotalFiles : Math.Max(fileIndex, completedSet.Count);
        var relPath = fileName.Replace('\\', '/').TrimStart('/');
        var rootSavedPath = Path.Combine(DownloadDirectory, relPath.Split('/')[0]);
        var finalDict = _sessionTransferredBytes.GetOrAdd(sessionId, _ => new System.Collections.Concurrent.ConcurrentDictionary<string, long>());
        finalDict[fileId] = bytesReadTotal;
        var finalSessionTransferred = finalDict.Values.Sum();
        var finalSessionTotal = prepareDto != null ? prepareDto.Files.Where(kv => selectedIds == null || selectedIds.Contains(kv.Key)).Sum(kv => kv.Value.Size) : totalBytes;
        Logger.Log($"[LocalSendServer] Received: {fileName} -> {targetPath}");
        ProgressChanged?.Invoke(this, new LocalSendProgressArgs(sessionId, senderAlias, fileId, fileName, bytesReadTotal, totalBytes, displayIndex, expectedTotalFiles, isFinished: true, isAllDone: isAllDone, savedPath: targetPath, rootSavedPath: rootSavedPath, sessionBytesTransferred: finalSessionTransferred, sessionTotalBytes: finalSessionTotal));
        LocalSendFileMetadataApplier.Apply(targetPath, metadata);
        FileReceived?.Invoke(this, (fileId, targetPath));
        if (isAllDone)
            UnregisterSession(sessionId);
        await LocalSendServerHelper.WriteResponseAsync(stream, 200).ConfigureAwait(false);
    }
    internal bool TryActivateSession(string sessionId, string senderIp, IReadOnlyDictionary<string, string> fileTokens,
        string? customDirectory, HashSet<string>? selectedFileIds) =>
        _receiveSessions.TryActivate(sessionId, senderIp, fileTokens, customDirectory, selectedFileIds);
    internal void RegisterUploadAuthorization(string sessionId, string senderIp, IReadOnlyDictionary<string, string> fileTokens) =>
        _receiveSessions.RegisterAuthorization(sessionId, senderIp, fileTokens);
    private bool TryStartUpload(string sessionId, string fileId, string token, string senderIp, bool v2,
        out LocalSendUploadContext context, out string error) =>
        _receiveSessions.TryStartUpload(sessionId, fileId, token, senderIp, v2, DownloadDirectory, out context, out error);
    private bool CompleteUploadAttempt(string sessionId, string fileId, LocalSendFileSaveStatus result) =>
        _receiveSessions.CompleteUpload(sessionId, fileId, result);
    internal Task<(bool Accepted, string? CustomDir, HashSet<string>? SelectedFileIds)> RequestUserAcceptanceAsync(string sessionId, PrepareUploadRequestDto dto, bool isAutoAccepted = false) => LocalSendServerSessionHelper.RequestAcceptanceAsync(this, sessionId, dto, isAutoAccepted);
    internal bool HasUploadRequestedHandler => UploadRequested != null;
    internal void InvokeUploadRequested(LocalSendUploadRequestArgs args) => UploadRequested?.Invoke(this, args);
    internal void InvokeDeviceRegistered(LocalSendDeviceInfo dto) => DeviceRegistered?.Invoke(this, dto);
    internal void InvokeShowRequested(IReadOnlyList<string>? files) => ShowRequested?.Invoke(this, files);
    public void Stop() { _cts?.Cancel(); try { _listener?.Stop(); } catch { } _listener = null; }
    public void Dispose()
    {
        Stop();
        IdentityCertificate?.Dispose();
        IdentityCertificate = null;
        Certificate = null;
    }
}
