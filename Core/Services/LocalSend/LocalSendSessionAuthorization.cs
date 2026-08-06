using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>Matches LocalSend's sender and protocol checks before a remote cancel request can affect a session.</summary>
internal static class LocalSendSessionAuthorization
{
    internal static bool TryCancel(LocalSendServer server, string? requestedSessionId, string senderIp, bool v2)
    {
        if (!server.HasActiveSessions)
            return server.TryCancelOutgoingSession(requestedSessionId, senderIp, v2);

        KeyValuePair<string, PrepareUploadRequestDto> session;
        if (v2 && !string.IsNullOrEmpty(requestedSessionId))
        {
            if (!server.TryGetActiveSession(requestedSessionId, out var dto) || dto == null)
                return false;
            session = new KeyValuePair<string, PrepareUploadRequestDto>(requestedSessionId, dto);
        }
        else
        {
            var sessions = server.GetActiveSessions();
            if (sessions.Length != 1 || (!v2 && sessions[0].Value.Info.Version != "1.0") || (v2 && server.HasUploadAuthorization(sessions[0].Key)))
                return false;
            session = sessions[0];
        }

        if (!string.Equals(session.Value.Info.IpAddress, LocalSendServerHelper.CleanIpAddress(senderIp), StringComparison.OrdinalIgnoreCase))
            return false;

        if (server.IsSessionCanceled(session.Key))
            return false;

        server.CancelSession(session.Key, notifySender: false);
        return true;
    }
}
