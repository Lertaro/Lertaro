using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Windows;
using Lertaro.App.Views.Controls.Dialogs;

namespace Lertaro.App.Services.Update;

// Downloads, signature-verifies, and installs a portable-zip update. Kept separate from UpdateChecker:
// installing is user-consented and has a different failure domain (crypto/filesystem/process elevation)
// than the periodic GitHub version check.
public class UpdateInstaller
{
    private static readonly Lazy<UpdateInstaller> _instance = new Lazy<UpdateInstaller>(() => new UpdateInstaller());
    public static UpdateInstaller Instance => _instance.Value;

    private readonly HttpClient _httpClient;

    private UpdateInstaller()
    {
        _httpClient = new HttpClient();
        // User-Agent header is strictly required by GitHub API
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Lertaro", "1.0.0"));
    }

    private const string PUBLIC_KEY_PEM =
        "-----BEGIN PUBLIC KEY-----\n" +
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE117370jTbSPgIwHLntC+Bi3SD6gJ\n" +
        "QfxAySjSpUWa6zy4n0YHVv/ZWXM9zQlF2LTqpQC0iHNdJNH+MKU9UvDMTQ==\n" +
        "-----END PUBLIC KEY-----";

    private bool VerifySignature(string filePath, string signaturePath)
    {
        try
        {
            var fileBytes = File.ReadAllBytes(filePath);
            var signatureBytes = File.ReadAllBytes(signaturePath);

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(PUBLIC_KEY_PEM);

            return ecdsa.VerifyData(fileBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception ex)
        {
            Core.Logger.Log($"[UpdateService] Signature verification encountered error: {ex.Message}", Core.LogLevel.Error);
            return false;
        }
    }

    /// <summary>
    /// Downloads the portable zip, extracts it, and triggers the portable-updater.bat.
    /// </summary>
    public async Task<bool> StartSilentUpdateAsync(string zipUrl, Action<double>? progressCallback = null)
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "LertaroUpdate");
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
            Directory.CreateDirectory(tempPath);

            var tempZipFile = Path.Combine(tempPath, "latest.zip");
            var tempSigFile = Path.Combine(tempPath, "latest.zip.sig");

            // Download zip file with progress report
            using (var response = await _httpClient.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(tempZipFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                var buffer = new byte[8192];
                var totalRead = 0L;
                int read;
                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read);
                    totalRead += read;
                    if (totalBytes != -1 && progressCallback != null)
                    {
                        progressCallback((double)totalRead / totalBytes);
                    }
                }
            }

            // Download signature file
            var sigUrl = zipUrl + ".sig";
            using (var sigResponse = await _httpClient.GetAsync(sigUrl))
            {
                sigResponse.EnsureSuccessStatusCode();
                using var sigFileStream = new FileStream(tempSigFile, FileMode.Create, FileAccess.Write, FileShare.None);
                await sigResponse.Content.CopyToAsync(sigFileStream);
            }

            // Verify signature before extracting
            if (!VerifySignature(tempZipFile, tempSigFile))
            {
                Core.Logger.Log("[UpdateService] Signature verification failed! The downloaded update package is not signed by a trusted key.", Core.LogLevel.Error);
                CustomMessageBox.Show(
                    TranslationManager.Instance["Update_SigVerificationFailedMessage"],
                    TranslationManager.Instance["Update_SigVerificationFailedTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }

            // Extract Zip
            var extractPath = Path.Combine(tempPath, "extracted");
            ZipFile.ExtractToDirectory(tempZipFile, extractPath);

            // Dynamically detect source path format (flat files vs wrapped in a Lertaro folder)
            var finalSourcePath = extractPath;
            var subDirs = Directory.GetDirectories(extractPath);
            if (subDirs.Length == 1 && Path.GetFileName(subDirs[0]).Equals("Lertaro", StringComparison.OrdinalIgnoreCase))
            {
                finalSourcePath = subDirs[0];
            }

            // Find batch updater
            var currentDir = AppDomain.CurrentDomain.BaseDirectory;
            var updaterBat = Path.Combine(currentDir, "portable-updater.bat");

            if (!File.Exists(updaterBat))
            {
                throw new FileNotFoundException("Updater script (portable-updater.bat) not found in application directory.");
            }

            // Launch batch updater in background with Admin privileges (elevated if not already)
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{updaterBat}\" \"{finalSourcePath}\" \"{currentDir.TrimEnd('\\')}\"\"",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                Verb = "runas" // Prompt for UAC elevation if not already running as admin
            };

            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            Core.Logger.Log($"[UpdateService] Auto update failed: {ex}", Core.LogLevel.Error);
            return false;
        }
    }
}
