using System.Security.Cryptography.X509Certificates;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>Creates LocalSend HTTP clients with the device identity and optional TLS certificate pinning.</summary>
internal static class LocalSendHttpClientFactory
{
    private static readonly HttpRequestOptionsKey<string> PeerFingerprintKey = new("LocalSendPeerFingerprint");

    internal static HttpClient Create(X509Certificate2 identityCertificate, string? expectedFingerprint = null,
        TimeSpan? timeout = null)
    {
        var normalizedExpected = NormalizeFingerprint(expectedFingerprint);
        var handler = new HttpClientHandler { UseProxy = false };
        handler.ClientCertificates.Add(identityCertificate);
        handler.ServerCertificateCustomValidationCallback = (request, certificate, _, _) =>
        {
            if (certificate == null || !LocalSendCertificate.IsValidPeerCertificate(certificate))
                return false;

            var actual = LocalSendCertificate.GetFingerprint(certificate);
            request.Options.Set(PeerFingerprintKey, actual);
            return normalizedExpected == null || string.Equals(actual, normalizedExpected, StringComparison.OrdinalIgnoreCase);
        };

        return new HttpClient(handler) { Timeout = timeout ?? Timeout.InfiniteTimeSpan };
    }

    internal static bool TryGetPeerFingerprint(HttpRequestMessage request, out string fingerprint) =>
        request.Options.TryGetValue(PeerFingerprintKey, out fingerprint!);

    internal static string? NormalizeFingerprint(string? fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            return null;
        return fingerprint.Replace(":", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
    }
}
