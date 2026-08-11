using System.Net;
using System.Text.Json;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>Handles the stateful prepare-upload exchange outside the low-level HTTP router.</summary>
internal static class LocalSendPrepareUploadHandler
{
    internal static async Task HandleAsync(LocalSendServer server, Stream stream, Dictionary<string, string> query,
        string body, EndPoint? remoteEndpoint, string? peerFingerprint, bool v2, CancellationToken token)
    {
        var clientIp = remoteEndpoint is IPEndPoint remoteIp ? LocalSendServerHelper.FormatIpAddress(remoteIp.Address) : string.Empty;
        query.TryGetValue("pin", out var requestPin);
        if (!server.CheckPin(clientIp, requestPin, out var pinStatus, out var pinError))
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, pinStatus, pinError).ConfigureAwait(false);
            return;
        }

        if (!LocalSendPrepareUploadRequestParser.TryParse(body, out var request) || request == null)
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, 400, "{\"message\":\"Request body malformed\"}").ConfigureAwait(false);
            return;
        }

        if (request.Files.Count == 0)
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, 400, "{\"message\":\"Request must contain at least one file\"}").ConfigureAwait(false);
            return;
        }

        if (peerFingerprint != null)
            request.Info.Fingerprint = peerFingerprint;

        var dto = new PrepareUploadRequestDto
        {
            Info = LocalSendProtocolMapper.ToDevice(request.Info, clientIp, server.DeviceInfo.Port, server.DeviceInfo.Protocol),
            Files = request.Files
        };
        var sessionId = Guid.NewGuid().ToString();
        if (!server.TryRegisterActiveSession(sessionId, dto))
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, 409, "{\"message\":\"Blocked by another session\"}").ConfigureAwait(false);
            return;
        }

        using var monitorCancellation = new CancellationTokenSource();
        using var serverStopRegistration = token.Register(() => server.CancelSession(sessionId, notifySender: false));
        var acceptance = server.RequestUserAcceptanceAsync(sessionId, dto, server.QuickSave);
        if (!acceptance.IsCompleted)
        {
            var disconnected = LocalSendPeerDisconnectMonitor.WaitAsync(stream, monitorCancellation.Token);
            if (await Task.WhenAny(acceptance, disconnected).ConfigureAwait(false) == acceptance)
                monitorCancellation.Cancel();
            if (await disconnected.ConfigureAwait(false))
            {
                server.CancelSession(sessionId, notifySender: false);
                await acceptance.ConfigureAwait(false);
                server.UnregisterSession(sessionId);
                return;
            }
        }

        var response = await acceptance.ConfigureAwait(false);
        if (!server.QuickSave && !response.Accepted || server.IsSessionCanceled(sessionId))
        {
            server.UnregisterSession(sessionId);
            await LocalSendServerHelper.WriteResponseAsync(stream, 403, "{\"message\":\"File request declined by recipient\"}").ConfigureAwait(false);
            return;
        }

        var fileTokens = request.Files.Keys
            .Where(id => response.SelectedFileIds == null || response.SelectedFileIds.Contains(id))
            .ToDictionary(id => id, _ => Guid.NewGuid().ToString("N"));
        if (fileTokens.Count == 0)
        {
            server.UnregisterSession(sessionId);
            await LocalSendServerHelper.WriteResponseAsync(stream, 204).ConfigureAwait(false);
            return;
        }

        if (!server.TryActivateSession(sessionId, clientIp, fileTokens, response.CustomDir, response.SelectedFileIds))
        {
            server.UnregisterSession(sessionId);
            await LocalSendServerHelper.WriteResponseAsync(stream, 403, "{\"message\":\"Cancelled by sender\"}").ConfigureAwait(false);
            return;
        }

        try
        {
            var payload = v2
                ? JsonSerializer.Serialize(new PrepareUploadResponseDto { SessionId = sessionId, Files = fileTokens })
                : JsonSerializer.Serialize(fileTokens);
            await LocalSendServerHelper.WriteResponseAsync(stream, 200, payload).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Log($"[LocalSendServer] Failed to send prepare-upload response to sender: {ex.Message}");
            server.CancelSession(sessionId);
        }
    }
}
