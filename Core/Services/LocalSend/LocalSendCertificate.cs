using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>Stores the self-signed certificate used for LocalSend HTTPS in the current user's data directory.</summary>
internal static class LocalSendCertificate
{
    private const string CertificateFileName = "localsend.pfx";
    private const X509KeyStorageFlags LoadFlags = X509KeyStorageFlags.Exportable;
    private static readonly TimeSpan RenewalWindow = TimeSpan.FromDays(30);
    private static readonly DateTimeOffset CertificateNotBefore = new(1975, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CertificateNotAfter = new(4095, 12, 31, 23, 59, 59, TimeSpan.Zero);

    internal static X509Certificate2 LoadOrCreate() => LoadOrCreate(Path.Combine(Logger.UserDataDir, CertificateFileName));

    internal static X509Certificate2 LoadOrCreate(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var loaded = X509CertificateLoader.LoadPkcs12(File.ReadAllBytes(path), password: null, LoadFlags);
                if (!NeedsRenewal(loaded, DateTimeOffset.UtcNow))
                    return loaded;

                loaded.Dispose();
            }
        }
        catch (CryptographicException)
        {
            // A damaged certificate cannot establish TLS, so replace it with a new identity.
        }

        using var generated = CreateEphemeral();
        var pfx = generated.Export(X509ContentType.Pfx);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("The certificate path has no parent directory."));
        File.WriteAllBytes(path, pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, password: null, LoadFlags);
    }

    internal static X509Certificate2 CreateEphemeral()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=LocalSend", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var generated = request.CreateSelfSigned(CertificateNotBefore, CertificateNotAfter);
        return X509CertificateLoader.LoadPkcs12(
            generated.Export(X509ContentType.Pfx), password: null, LoadFlags);
    }

    internal static bool NeedsRenewal(X509Certificate2 certificate, DateTimeOffset now)
    {
        var utcNow = now.UtcDateTime;
        return !certificate.HasPrivateKey ||
               utcNow < certificate.NotBefore.ToUniversalTime() ||
               certificate.NotAfter.ToUniversalTime() <= utcNow.Add(RenewalWindow);
    }

    internal static string GetFingerprint(X509Certificate2 certificate) =>
        Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256));

    internal static bool IsValidPeerCertificate(X509Certificate2 certificate)
    {
        var now = DateTime.UtcNow;
        if (now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime())
            return false;

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(certificate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        return chain.Build(certificate);
    }
}
