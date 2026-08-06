using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>Stores the self-signed certificate used for LocalSend HTTPS in the current user's data directory.</summary>
internal static class LocalSendCertificate
{
    private const string CertificateFileName = "localsend.pfx";

    internal static X509Certificate2 LoadOrCreate() => LoadOrCreate(Path.Combine(Logger.UserDataDir, CertificateFileName));

    internal static X509Certificate2 LoadOrCreate(string path)
    {
        try
        {
            if (File.Exists(path))
                return X509CertificateLoader.LoadPkcs12(File.ReadAllBytes(path), password: null, X509KeyStorageFlags.Exportable);
        }
        catch (CryptographicException)
        {
            // A damaged certificate cannot establish TLS, so replace it with a new identity.
        }

        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=LocalSend", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        var pfx = generated.Export(X509ContentType.Pfx);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("The certificate path has no parent directory."));
        File.WriteAllBytes(path, pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, password: null, X509KeyStorageFlags.Exportable);
    }

    internal static string GetFingerprint(X509Certificate2 certificate) =>
        Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256));
}
