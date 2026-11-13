using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;

using Lertaro.App.Views.Controls.Dialogs;

using Lertaro.Core.Services.Search;

using Lertaro.Core.Services.Update;

namespace Lertaro.App.Services.Update;

// Downloads and signature-verifies a portable-zip update, then hands it to the background service to
// install. Kept separate from UpdateChecker: the two have different failure domains
// (crypto/filesystem/service IPC vs network/JSON parsing), and a silent update runs this half without
// any prompt at all (see UpdateCheckService's consent-by-setting path).
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
    /// <param name="zipUrls">
    /// The addresses to try in order, as <see cref="ReleaseInfo.DownloadUrls"/> builds them: the user's
    /// GitHub mirrors first, the release's own address last. A GitLab release arrives as a single entry.
    /// </param>
    /// <returns>
    /// Ok is true only once the updater is actually running, which is also the caller's cue to exit so the
    /// files being replaced stop being locked. False means nothing was installed and this process should stay
    /// up; <c>Error</c> then carries the reason to show, when there is one worth showing (the signature
    /// failure has already shown its own dialog).
    /// </returns>
    public async Task<(bool Ok, string? Error)> StartSilentUpdateAsync(IReadOnlyList<string> zipUrls, Action<double>? progressCallback = null)
    {
        // A fresh per-run directory rather than the fixed %TEMP%\LertaroUpdate it used to be: a fixed name
        // is up for grabs to whichever process creates it first, and whatever was read out of it then went
        // into the install directory.
        var stagingDir = UpdatePackage.CreateStagingDirectory();
        var tempZipFile = Path.Combine(stagingDir, UpdatePackage.ZipFileName);
        var tempSigFile = Path.Combine(stagingDir, UpdatePackage.SignatureFileName);

        try
        {
            // Only a download that never arrived moves to the next address. Bytes that did arrive and then
            // fail verification below are a security verdict, not a network problem, so they are reported
            // rather than retried against another host. The last attempt's exception is deliberately left
            // to escape to the catch below, which is what reports it.
            //
            // ponytail: an attempt inherits HttpClient's own timeout, so a mirror that accepts the
            // connection and then stalls costs that whole timeout before the next one is tried. The upgrade
            // path is a per-attempt CancellationTokenSource, which needs a bound that does not also cap a
            // legitimately slow zip read.
            for (var attempt = 0; attempt < zipUrls.Count; attempt++)
            {
                try
                {
                    await DownloadPackageAsync(zipUrls[attempt], tempZipFile, tempSigFile, progressCallback);
                    break;
                }
                catch (Exception ex) when (attempt < zipUrls.Count - 1)
                {
                    Core.Logger.Log($"[UpdateService] Download from {zipUrls[attempt]} failed, trying the next address: {ex.Message}", Core.LogLevel.Warn);
                }
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
                return (false, null);
            }

            using var searchService = new SearchService();
            var (ok, error) = await searchService.RequestApplyUpdateAsync(stagingDir).ConfigureAwait(false);
            if (!ok)
            {
                // Covers the service not running, an App/Service pair that disagrees about the pipe
                // protocol, and the refusals the handler makes in words it chooses itself (no administrator,
                // updater missing, an update already running). The About page is asked for by a user, so it
                // shows that sentence; the startup path only logs it.
                Core.Logger.Log($"[UpdateService] Service refused the update: {error}", Core.LogLevel.Error);
                DeleteStagingDirectory(stagingDir);
                return (false, error);
            }

            // The service has already read the package and unpacked it under the install directory.
            DeleteStagingDirectory(stagingDir);
            return (true, null);
        }
        catch (Exception ex)
        {
            Core.Logger.Log($"[UpdateService] Auto update failed: {ex}", Core.LogLevel.Error);
            DeleteStagingDirectory(stagingDir);
            return (false, ex.Message);
        }
    }

    // Throws on any transport or HTTP failure, which is what lets the caller decide whether another
    // address is left. FileMode.Create truncates whatever a previous attempt wrote, so a half-downloaded
    // zip is never verified or handed to the service.
    private async Task DownloadPackageAsync(string zipUrl, string zipFile, string signatureFile, Action<double>? progressCallback)
    {
        // Decided once per address rather than put on the shared client: only a GitLab address may see the
        // token, so the GitHub line, its mirrors, and anything else the release JSON names go out without it.
        var token = TokenFor(zipUrl);

        using (var response = await GetAsync(zipUrl, token))
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(zipFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
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

        // Both release lines publish the signature beside the zip under exactly this name, so appending
        // ".sig" follows the zip through whichever mirror answered for it.
        using var sigResponse = await GetAsync(zipUrl + ".sig", token);
        sigResponse.EnsureSuccessStatusCode();
        using var sigFileStream = new FileStream(signatureFile, FileMode.Create, FileAccess.Write, FileShare.None);
        await sigResponse.Content.CopyToAsync(sigFileStream);
    }

    // The same "PRIVATE-TOKEN" request the GitLab docs hand out for a release asset download, e.g.
    // `curl --location --header "PRIVATE-TOKEN: <token>" ".../releases/v0.1/downloads/bin/asset.exe"`.
    // An empty token adds no header at all, which is what keeps a public GitLab download (and every
    // other address) exactly as anonymous as it was.
    private async Task<HttpResponseMessage> GetAsync(string url, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (token.Length > 0) request.Headers.Add("PRIVATE-TOKEN", token);

        // The caller disposes the response; disposing the request here is fine, the response has already
        // been handed back with its headers.
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    /// <summary>
    /// Whether this address is GitLab's, and therefore the only kind that may carry the user's token.
    /// </summary>
    /// <remarks>
    /// Not about mirrors: GitLab never goes through one. <see cref="ReleaseInfo.Mirrors"/> is filled for a
    /// GitHub release only, so a GitLab release arrives with its own single address and the mirror boxes
    /// cannot reach it. The address is still checked against the host rather than assumed, because the
    /// URL itself comes out of the release JSON: a release whose asset links point somewhere other than
    /// <see cref="UpdateChecker.GitLabHost"/> would otherwise be handed the user's token, and it is the
    /// host that answers, not the fact that the release came from GitLab. https only, so an entry naming
    /// the right host over plain http cannot talk the token onto the wire in clear text.
    /// </remarks>
    internal static bool IsGitLabAddress(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals(UpdateChecker.GitLabHost, StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith("." + UpdateChecker.GitLabHost, StringComparison.OrdinalIgnoreCase));

    private static string TokenFor(string url) =>
        IsGitLabAddress(url)
            ? Core.UpdateMirrorSettings.NormalizeToken(Core.UserSettings.Load().GitLabUpdateToken)
            : string.Empty;

    // Best effort. What's left behind is a few megabytes in this user's own temp directory, which is the
    // least harmful place in this flow to be untidy. Always this process's job, on success too: the service
    // runs as LocalSystem and deliberately never deletes a directory a caller named.
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
