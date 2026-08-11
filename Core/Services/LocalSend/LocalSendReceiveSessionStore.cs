using System.Collections.Concurrent;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>
/// Owns the single receive-session slot and its upload authorization state.
/// Split out to keep LocalSendServer under the repository's per-file line limit.
/// </summary>
internal sealed class LocalSendReceiveSessionStore
{
    private readonly ConcurrentDictionary<string, PrepareUploadRequestDto> _sessions = new();
    private readonly ConcurrentDictionary<string, LocalSendUploadAuthorization> _authorizations = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = new();
    private readonly ConcurrentDictionary<string, byte> _canceledSessions = new();
    private readonly ConcurrentDictionary<string, string> _customDirectories = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _selectedFileIds = new();
    private readonly object _lock = new();

    internal bool HasSessions => !_sessions.IsEmpty;
    internal bool HasAuthorization(string sessionId) => _authorizations.ContainsKey(sessionId);
    internal bool IsCanceled(string sessionId) => !string.IsNullOrEmpty(sessionId) && _canceledSessions.ContainsKey(sessionId);
    internal bool TryGet(string sessionId, out PrepareUploadRequestDto? request) => _sessions.TryGetValue(sessionId, out request);
    internal KeyValuePair<string, PrepareUploadRequestDto>[] GetAll() => _sessions.ToArray();

    internal bool TryRegister(string sessionId, PrepareUploadRequestDto request, bool windowOpen)
    {
        lock (_lock)
        {
            if (windowOpen || !_sessions.IsEmpty)
                return false;
            _sessions[sessionId] = request;
            _cancellations[sessionId] = new CancellationTokenSource();
            return true;
        }
    }

    internal bool TryActivate(string sessionId, string senderIp, IReadOnlyDictionary<string, string> fileTokens,
        string? customDirectory, HashSet<string>? selectedFileIds)
    {
        lock (_lock)
        {
            if (!_sessions.ContainsKey(sessionId) || IsCanceled(sessionId))
                return false;
            _authorizations[sessionId] = new LocalSendUploadAuthorization(senderIp, fileTokens);
            if (!string.IsNullOrEmpty(customDirectory))
                _customDirectories[sessionId] = customDirectory;
            if (selectedFileIds != null)
                _selectedFileIds[sessionId] = selectedFileIds;
            return true;
        }
    }

    internal (bool Canceled, PrepareUploadRequestDto? Request) Cancel(string sessionId)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out var request) || !_canceledSessions.TryAdd(sessionId, 0))
                return (false, null);
            if (_cancellations.TryGetValue(sessionId, out var cancellation))
                cancellation.Cancel();
            if (_authorizations.ContainsKey(sessionId))
                UnregisterCore(sessionId);
            return (true, request);
        }
    }

    internal void Unregister(string sessionId)
    {
        lock (_lock)
            UnregisterCore(sessionId);
    }

    internal bool TryStartUpload(string sessionId, string fileId, string token, string senderIp, bool v2,
        string defaultDownloadDirectory, out LocalSendUploadContext context, out string error)
    {
        lock (_lock)
        {
            context = null!;
            if (!LocalSendUploadAuthorizationChecker.TryAuthorize(_authorizations, sessionId, fileId, token,
                senderIp, v2, out var resolvedSessionId, out error))
                return false;
            if (!_sessions.TryGetValue(resolvedSessionId, out var request) ||
                !request.Files.TryGetValue(fileId, out var file) ||
                !_cancellations.TryGetValue(resolvedSessionId, out var cancellation) ||
                v2 && (!_authorizations.TryGetValue(resolvedSessionId, out var authorization) || !authorization.TryBeginUpload(fileId)))
            {
                error = v2 ? "Invalid token or IP address" : "Invalid token";
                return false;
            }

            var directory = _customDirectories.TryGetValue(resolvedSessionId, out var customDirectory) &&
                !string.IsNullOrEmpty(customDirectory) ? customDirectory : defaultDownloadDirectory;
            _selectedFileIds.TryGetValue(resolvedSessionId, out var selectedIds);
            context = new LocalSendUploadContext(resolvedSessionId, request, file, selectedIds, directory, cancellation.Token);
            return true;
        }
    }

    internal bool CompleteUpload(string sessionId, string fileId, LocalSendFileSaveStatus result) =>
        _authorizations.TryGetValue(sessionId, out var authorization) && authorization.CompleteUpload(fileId, result);

    internal void RegisterAuthorization(string sessionId, string senderIp, IReadOnlyDictionary<string, string> fileTokens) =>
        _authorizations[sessionId] = new LocalSendUploadAuthorization(senderIp, fileTokens);

    private void UnregisterCore(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
        _authorizations.TryRemove(sessionId, out _);
        _customDirectories.TryRemove(sessionId, out _);
        _selectedFileIds.TryRemove(sessionId, out _);
        _canceledSessions.TryRemove(sessionId, out _);
        if (_cancellations.TryRemove(sessionId, out var cancellation))
            cancellation.Dispose();
    }
}
