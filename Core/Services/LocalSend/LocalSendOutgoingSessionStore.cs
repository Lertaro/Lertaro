using System.Collections.Concurrent;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>
/// Tracks active outbound transfers so a receiver's cancel callback can stop the matching HTTP upload.
/// </summary>
internal sealed class LocalSendOutgoingSessionStore
{
    private readonly ConcurrentDictionary<string, LocalSendOutgoingSession> _sessions = new();

    internal LocalSendOutgoingSession Start(string remoteIp, string? remoteSessionId, bool legacy)
    {
        var key = remoteSessionId ?? Guid.NewGuid().ToString("N");
        var session = new LocalSendOutgoingSession(key, remoteIp, legacy, () => _sessions.TryRemove(key, out _));
        if (!_sessions.TryAdd(key, session))
            throw new InvalidOperationException("LocalSend outbound session is already registered.");
        return session;
    }

    internal bool TryCancel(string? remoteSessionId, string senderIp, bool v2)
    {
        LocalSendOutgoingSession? session;
        if (v2)
        {
            if (string.IsNullOrEmpty(remoteSessionId) || !_sessions.TryGetValue(remoteSessionId, out session))
                return false;
        }
        else
        {
            var candidates = _sessions.Values.Where(candidate => candidate.Legacy).ToArray();
            if (candidates.Length != 1)
                return false;
            session = candidates[0];
        }

        if (!string.Equals(session.RemoteIp, LocalSendServerHelper.CleanIpAddress(senderIp), StringComparison.OrdinalIgnoreCase))
            return false;

        return session.TryCancel();
    }
}

/// <summary>Owns the cancellation signal for one LocalSend outbound transfer.</summary>
internal sealed class LocalSendOutgoingSession : IDisposable
{
    private readonly Action _unregister;
    private int _disposed;

    internal LocalSendOutgoingSession(string remoteSessionId, string remoteIp, bool legacy, Action unregister)
    {
        RemoteSessionId = remoteSessionId;
        RemoteIp = LocalSendServerHelper.CleanIpAddress(remoteIp);
        Legacy = legacy;
        _unregister = unregister;
    }

    internal string RemoteSessionId { get; }
    internal string RemoteIp { get; }
    internal bool Legacy { get; }
    internal CancellationTokenSource Cancellation { get; } = new();

    internal bool TryCancel()
    {
        if (Cancellation.IsCancellationRequested)
            return false;
        Cancellation.Cancel();
        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _unregister();
        Cancellation.Dispose();
    }
}
