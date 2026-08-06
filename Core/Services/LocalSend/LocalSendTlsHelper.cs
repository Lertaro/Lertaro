using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>Wraps accepted LocalSend sockets in TLS only when HTTPS is enabled.</summary>
internal static class LocalSendTlsHelper
{
    internal static async Task<Stream> CreateServerStreamAsync(TcpClient client, X509Certificate2? certificate, CancellationToken token)
    {
        var networkStream = client.GetStream();
        if (certificate == null)
            return networkStream;

        var secureStream = new SslStream(networkStream, leaveInnerStreamOpen: false);
        await secureStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = certificate,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        }, token).ConfigureAwait(false);
        return secureStream;
    }
}
