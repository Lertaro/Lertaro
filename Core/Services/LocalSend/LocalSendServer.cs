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

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PrepareUploadRequestDto> _activeSessions = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, LocalSendUploadAuthorization> _uploadAuthorizations = new();
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
    public string? ReceivePin { get; set; }
    public string ShowToken { get; set; } = string.Empty;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _pinAttempts = new();

    internal bool CheckPin(string clientIp, string? requestPin, out int statusCode, out string? jsonBody)
        => LocalSendServerHelper.CheckPin(ReceivePin, _pinAttempts, clientIp, requestPin, out statusCode, out jsonBody);

    public event EventHandler<LocalSendUploadRequestArgs>? UploadRequested;
    public event EventHandler<(string FileId, string Path)>? FileReceived;
    public event EventHandler<LocalSendDeviceInfo>? DeviceRegistered;
    public event EventHandler<IReadOnlyList<string>?>? ShowRequested;

    public int ActualPort { get; private set; }
    public X509Certificate2? Certificate { get; set; }
    public bool IsBusy => LocalSendServiceManager.Instance.IsWindowOpen || !_activeSessions.IsEmpty;

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
                using var stream = await LocalSendTlsHelper.CreateServerStreamAsync(client, Certificate, token).ConfigureAwait(false);
                await LocalSendServerHandler.ProcessAsync(this, stream, client.Client.RemoteEndPoint, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Log($"[LocalSendServer] Client handling error: {ex.Message}", LogLevel.Debug);
            }
        }
    }

    internal void RegisterActiveSession(string sessionId, PrepareUploadRequestDto dto) => _activeSessions[sessionId] = dto;

    public event EventHandler<LocalSendProgressArgs>? ProgressChanged;
    public event EventHandler<string>? SessionCanceled;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _canceledSessions = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<string, byte>> _sessionCompletedFiles = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<string, long>> _sessionTransferredBytes = new();

    public void CancelSession(string sessionId, bool notifySender = false)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        if (notifySender && _activeSessions.TryGetValue(sessionId, out var prepareDto))
        {
            _ = Task.Run(() => LocalSendServerHelper.NotifySenderCanceledAsync(prepareDto.Info, sessionId));
        }
        if (_canceledSessions.TryAdd(sessionId, 0))
        {
            SessionCanceled?.Invoke(this, sessionId);
        }
    }

    public void CancelAllSessions()
    {
        var activeIds = _activeSessions.Keys.ToList();
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
        _activeSessions.TryRemove(sessionId, out _);
        _uploadAuthorizations.TryRemove(sessionId, out _);
        _sessionCustomDirectories.TryRemove(sessionId, out _);
        _sessionSelectedFileIds.TryRemove(sessionId, out _);
        _sessionCompletedFiles.TryRemove(sessionId, out _);
        _sessionTransferredBytes.TryRemove(sessionId, out _);
        _canceledSessions.TryRemove(sessionId, out _);
    }

    public bool IsSessionCanceled(string sessionId) =>
        !string.IsNullOrEmpty(sessionId) && _canceledSessions.ContainsKey(sessionId);

    internal bool HasActiveSessions => !_activeSessions.IsEmpty;
    internal bool HasUploadAuthorization(string sessionId) => _uploadAuthorizations.ContainsKey(sessionId);
    internal bool TryGetActiveSession(string sessionId, out PrepareUploadRequestDto? dto) => _activeSessions.TryGetValue(sessionId, out dto);
    internal KeyValuePair<string, PrepareUploadRequestDto>[] GetActiveSessions() => _activeSessions.ToArray();
    internal LocalSendOutgoingSession StartOutgoingSession(string remoteIp, string? remoteSessionId, bool legacy) => _outgoingSessions.Start(remoteIp, remoteSessionId, legacy);
    internal bool TryCancelOutgoingSession(string? remoteSessionId, string senderIp, bool v2) => _outgoingSessions.TryCancel(remoteSessionId, senderIp, v2);

    internal async Task HandleUploadAsync(
        Stream stream, Stream requestBody, string sessionId, string fileId, string token, string senderIp, bool v2)
    {
        if (!TryAuthorizeUpload(sessionId, fileId, token, senderIp, v2, out sessionId, out var authorizationError))
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, 403, $"{{\"message\":\"{authorizationError}\"}}").ConfigureAwait(false);
            return;
        }

        if (IsSessionCanceled(sessionId))
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, 409, "{\"message\":\"Recipient is in wrong state\"}").ConfigureAwait(false);
            return;
        }

        var fileName = $"{fileId}.bin";
        var senderAlias = "LocalSend";
        LocalSendFileMetadataDto? metadata = null;
        long totalBytes = 0;
        var fileIndex = 1;
        var totalFiles = 1;

        if (_activeSessions.TryGetValue(sessionId, out var prepareDto))
        {
            senderAlias = prepareDto.Info.Alias;
            totalFiles = prepareDto.Files.Count;
            var keys = prepareDto.Files.Keys.ToList();
            fileIndex = Math.Max(1, keys.IndexOf(fileId) + 1);

            if (prepareDto.Files.TryGetValue(fileId, out var fileDto))
            {
                fileName = fileDto.FileName;
                totalBytes = fileDto.Size;
                metadata = fileDto.Metadata;
            }
        }

        var baseDownloadDir = _sessionCustomDirectories.TryGetValue(sessionId, out var customDir) && !string.IsNullOrEmpty(customDir) ? customDir : DownloadDirectory;
        var targetPath = LocalSendServerHelper.ResolveTargetPath(baseDownloadDir, fileName);
        if (targetPath == null)
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, 403).ConfigureAwait(false);
            return;
        }

        var buffer = new byte[1024 * 1024];
        long bytesReadTotal = 0;
        long lastFlushedBytes = 0;
        long lastProgressTimeMs = 0;
        var progressStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var isSuccess = false;
        var selectedIds = _sessionSelectedFileIds.TryGetValue(sessionId, out var sIds) ? sIds : null;

        try
        {
            using (var dest = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 1024, useAsync: true))
            {
                int bytesRead;
                while ((bytesRead = await requestBody.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                {
                    if (IsSessionCanceled(sessionId)) break;
                    await dest.WriteAsync(buffer.AsMemory(0, bytesRead)).ConfigureAwait(false);
                    bytesReadTotal += bytesRead;
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
                }
            }
            if (!IsSessionCanceled(sessionId))
            {
                isSuccess = true;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[LocalSendServer] Error writing upload stream for {fileName}: {ex.GetType().Name} - {ex.Message}", LogLevel.Error);
        }
        finally
        {
            if (!isSuccess) LocalSendServerHelper.TryDeleteFile(targetPath);
        }

        if (!isSuccess)
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, 500, "{\"message\":\"Could not save file. Check receiving device for more information.\"}").ConfigureAwait(false);
            return;
        }

        var completedSet = _sessionCompletedFiles.GetOrAdd(sessionId, _ => new System.Collections.Concurrent.ConcurrentDictionary<string, byte>());
        completedSet[fileId] = 0;

        var expectedTotalFiles = selectedIds != null ? selectedIds.Count : totalFiles;
        var isAllDone = completedSet.Count >= expectedTotalFiles;
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
        await LocalSendServerHelper.WriteResponseAsync(stream, 200).ConfigureAwait(false);
        if (isAllDone)
            UnregisterSession(sessionId);
    }
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _sessionCustomDirectories = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, HashSet<string>> _sessionSelectedFileIds = new();
    internal void RegisterCustomDirectory(string sessionId, string? customDir) { if (!string.IsNullOrEmpty(customDir)) _sessionCustomDirectories[sessionId] = customDir; }
    internal void RegisterSelectedFileIds(string sessionId, HashSet<string>? selectedIds) { if (selectedIds != null) _sessionSelectedFileIds[sessionId] = selectedIds; }
    internal void RegisterUploadAuthorization(string sessionId, string senderIp, IReadOnlyDictionary<string, string> fileTokens) => _uploadAuthorizations[sessionId] = new LocalSendUploadAuthorization(senderIp, fileTokens);
    internal bool TryAuthorizeUpload(string sessionId, string fileId, string token, string senderIp, bool v2, out string resolvedSessionId, out string error) =>
        LocalSendUploadAuthorizationChecker.TryAuthorize(_uploadAuthorizations, sessionId, fileId, token, senderIp, v2, out resolvedSessionId, out error);
    internal Task<(bool Accepted, string? CustomDir, HashSet<string>? SelectedFileIds)> RequestUserAcceptanceAsync(string sessionId, PrepareUploadRequestDto dto, bool isAutoAccepted = false) => LocalSendServerSessionHelper.RequestAcceptanceAsync(this, sessionId, dto, isAutoAccepted);
    internal bool HasUploadRequestedHandler => UploadRequested != null;
    internal void InvokeUploadRequested(LocalSendUploadRequestArgs args) => UploadRequested?.Invoke(this, args);
    internal void InvokeDeviceRegistered(LocalSendDeviceInfo dto) => DeviceRegistered?.Invoke(this, dto);
    internal void InvokeShowRequested(IReadOnlyList<string>? files) => ShowRequested?.Invoke(this, files);
    public void Stop() { _cts?.Cancel(); try { _listener?.Stop(); } catch { } _listener = null; }
    public void Dispose() => Stop();
}
