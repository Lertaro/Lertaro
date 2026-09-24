using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;

using Lertaro.App.Views.Controls.Dialogs;

using Lertaro.Core.Services.Search;

using Lertaro.Core.Services.Update;

namespace Lertaro.App.Services.Update;

// Downloads and signature-verifies a portable-zip update, then hands it to the background service to
// install. Kept separate from UpdateChecker: installing is user-consented and has a different failure
// domain (crypto/filesystem/service IPC) than the periodic GitHub version check.
//
// This process no longer performs the installation itself, and that is the whole point. It runs as the
// invoker, so writing into Program Files had to be paid for with a runas verb -- a UAC prompt on an update
// path named "silent", and a second one when the script it launched checked for elevation and asked again.
// The service is already LocalSystem and the App is already allowed to reach it without elevation, so the
// elevated half lives there and this half stops at the download.
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

    /// <summary>
    /// Downloads the portable zip, verifies it, and asks the service to install it.
    /// </summary>
    /// <returns>
    /// True only once the updater is actually running, which is also the caller's cue to exit so the files
    /// being replaced stop being locked. False means nothing was installed and this process should stay up.
    /// </returns>
    public async Task<bool> StartSilentUpdateAsync(string zipUrl, Action<double>? progressCallback = null)
    {
        // A fresh per-run directory rather than the fixed %TEMP%\LertaroUpdate it used to be: a fixed name
        // is up for grabs to whichever process creates it first, and whatever was read out of it then went
        // into the install directory.
        var stagingDir = UpdatePackage.CreateStagingDirectory();
        var tempZipFile = Path.Combine(stagingDir, UpdatePackage.ZipFileName);
        var tempSigFile = Path.Combine(stagingDir, UpdatePackage.SignatureFileName);

        try
        {
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

            // Checked here so a bad package is reported while there is still a window on screen to report
            // it in. The service checks again over the bytes it is about to install, because this process
            // can't promise those are the same bytes.
            if (!UpdatePackage.Verify(tempZipFile, tempSigFile))
            {
                Core.Logger.Log("[UpdateService] Signature verification failed! The downloaded update package is not signed by a trusted key.", Core.LogLevel.Error);
                CustomMessageBox.Show(
                    TranslationManager.Instance["Update_SigVerificationFailedMessage"],
                    TranslationManager.Instance["Update_SigVerificationFailedTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                DeleteStagingDirectory(stagingDir);
                return false;
            }

            using var searchService = new SearchService();
            var (ok, error) = await searchService.RequestApplyUpdateAsync(stagingDir).ConfigureAwait(false);
            if (!ok)
            {
                // Covers the service not running and an App/Service pair that disagrees about the pipe
                // protocol. Both mean "no update today", which is not something to bother a dialog about:
                // the next startup retries it, and the About page reports it to whoever asked.
                Core.Logger.Log($"[UpdateService] Service refused the update: {error}", Core.LogLevel.Error);
                DeleteStagingDirectory(stagingDir);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Core.Logger.Log($"[UpdateService] Auto update failed: {ex}", Core.LogLevel.Error);
            DeleteStagingDirectory(stagingDir);
            return false;
        }
    }

    // Best effort. What's left behind is a few megabytes in this user's own temp directory, which is the
    // least harmful place in this flow to be untidy; the service deletes the directory itself once it has
    // unpacked the payload from it.
    private static void DeleteStagingDirectory(string stagingDir)
    {
        try
        {
            if (Directory.Exists(stagingDir))
                Directory.Delete(stagingDir, true);
        }
        catch (Exception ex)
        {
            Core.Logger.Log($"[UpdateService] Could not clean up {stagingDir}: {ex.Message}", Core.LogLevel.Warn);
        }
    }
}
