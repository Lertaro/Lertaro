using System.Net;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>Runs successive HTTP/1.x requests over one LocalSend TCP connection until either peer closes it.</summary>
internal static class LocalSendHttpConnection
{
    internal static async Task ProcessAsync(LocalSendServer server, Stream stream, EndPoint? remoteEndpoint,
        string? peerFingerprint, CancellationToken token)
    {
        while (!token.IsCancellationRequested && await LocalSendServerHandler.ProcessRequestAsync(
            server, stream, remoteEndpoint, peerFingerprint, token).ConfigureAwait(false)) { }
    }
}
