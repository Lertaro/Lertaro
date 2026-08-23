using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

/// <summary>
/// Downloads and extracts the official embeddable Python package for Flow.Launcher Python plugins.
/// Matches Flow.Launcher's PythonEmbeded layout and configuration.
/// </summary>
public static class FlowPythonDownloader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    public static async Task<string?> DownloadAndSetupEmbeddedPythonAsync(string targetDir)
    {
        if (Directory.Exists(targetDir))
        {
            var existingExe = FindPythonInDir(targetDir);
            if (existingExe != null)
                return existingExe;
        }

        var url = GetDownloadUrl();
        var tempZip = Path.Combine(Path.GetTempPath(), $"python_embed_{Guid.NewGuid():N}.zip");

        try
        {
            Directory.CreateDirectory(targetDir);

            using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs).ConfigureAwait(false);
            }

            ZipFile.ExtractToDirectory(tempZip, targetDir, overwriteFiles: true);

            // Flow.Launcher requirement: enable 'import site' in ._pth file to allow package loading
            EnableImportSiteInPthFiles(targetDir);

            return FindPythonInDir(targetDir);
        }
        catch
        {
            return null;
        }
        finally
        {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
        }
    }

    private static string GetDownloadUrl()
    {
        var archSuffix = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "embed-arm64",
            Architecture.X86 => "embed-win32",
            _ => "embed-amd64"
        };

        return $"https://www.python.org/ftp/python/3.11.9/python-3.11.9-{archSuffix}.zip";
    }

    private static void EnableImportSiteInPthFiles(string targetDir)
    {
        try
        {
            foreach (var pthFile in Directory.GetFiles(targetDir, "*._pth"))
            {
                var content = File.ReadAllText(pthFile);
                if (content.Contains("#import site"))
                {
                    content = content.Replace("#import site", "import site");
                    File.WriteAllText(pthFile, content);
                }
                else if (!content.Contains("import site"))
                {
                    File.AppendAllText(pthFile, Environment.NewLine + "import site" + Environment.NewLine);
                }
            }
        }
        catch { }
    }

    public static string? FindPythonInDir(string dir)
    {
        if (!Directory.Exists(dir))
            return null;

        var candidates = new[] { "pythonw.exe", "python.exe" };
        foreach (var name in candidates)
        {
            var full = Path.Combine(dir, name);
            if (File.Exists(full))
                return full;
        }

        return null;
    }
}
