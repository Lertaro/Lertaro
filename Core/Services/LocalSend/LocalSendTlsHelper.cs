using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>Wraps accepted LocalSend sockets in TLS only when HTTPS is enabled.</summary>
internal static class LocalSendTlsHelper
{
    internal static async Task<LocalSendTlsConnection> CreateServerStreamAsync(
        TcpClient client, X509Certificate2? certificate, CancellationToken token)
    {
        var networkStream = client.GetStream();
        if (certificate == null)
            return new LocalSendTlsConnection(networkStream, null);

        var secureStream = new SslStream(networkStream, leaveInnerStreamOpen: false,
            (_, peerCertificate, _, _) => ValidatePeerCertificate(peerCertificate));
        await secureStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = certificate,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            ClientCertificateRequired = true,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        }, token).ConfigureAwait(false);
        using var peerCertificate = secureStream.RemoteCertificate == null
            ? null
            : new X509Certificate2(secureStream.RemoteCertificate);
        var fingerprint = peerCertificate == null ? null : LocalSendCertificate.GetFingerprint(peerCertificate);
        return new LocalSendTlsConnection(secureStream, fingerprint);
    }

    private static bool ValidatePeerCertificate(X509Certificate? certificate)
    {
        if (certificate == null)
            return false;
        using var peer = new X509Certificate2(certificate);
        return LocalSendCertificate.IsValidPeerCertificate(peer);
    }
}

internal sealed record LocalSendTlsConnection(Stream Stream, string? PeerFingerprint);
