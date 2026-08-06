using System.Collections.Concurrent;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>Applies the protocol's ordered upload authorization checks and their corresponding error messages.</summary>
internal static class LocalSendUploadAuthorizationChecker
{
    internal static bool TryAuthorize(ConcurrentDictionary<string, LocalSendUploadAuthorization> authorizations,
        string sessionId, string fileId, string token, string senderIp, bool v2, out string resolvedSessionId, out string error)
    {
        resolvedSessionId = sessionId;
        if (!v2 && string.IsNullOrEmpty(resolvedSessionId) && authorizations.Count == 1)
            resolvedSessionId = authorizations.Keys.Single();
        if (!authorizations.TryGetValue(resolvedSessionId, out var authorization))
        {
            error = v2 ? "Invalid session id" : "Invalid token";
            return false;
        }
        if (!authorization.MatchesSender(senderIp))
        {
            error = $"Invalid IP address: {LocalSendServerHelper.CleanIpAddress(senderIp)}";
            return false;
        }
        if (!authorization.AllowsToken(fileId, token))
        {
            error = "Invalid token";
            return false;
        }
        error = string.Empty;
        return true;
    }
}
