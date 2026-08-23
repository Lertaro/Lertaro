using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

/// <summary>
/// Downloads and extracts the official embeddable Python package for Flow plugins.
/// Configures import site and sitecustomize.py hook for seamless execution in FlowPlugins.
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
            {
                EnsureSiteCustomizeInstalled(targetDir);
                return existingExe;
            }
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

            // Flow requirement: enable 'import site' in ._pth file to allow package loading
            EnableImportSiteInPthFiles(targetDir);
            EnsureSiteCustomizeInstalled(targetDir);

            var pyExe = FindPythonInDir(targetDir);
            if (pyExe != null)
            {
                await FlowPipManager.EnsurePipInstalledAsync(pyExe).ConfigureAwait(false);
            }

            return pyExe;
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

    public static void EnsureSiteCustomizeInstalled(string targetDir)
    {
        try
        {
            var siteCustomizePath = Path.Combine(targetDir, "sitecustomize.py");
            const string code = @"import sys, os, importlib.abc, importlib.machinery
from pathlib import Path

class _FloxMetaFinder(importlib.abc.MetaPathFinder):
    def find_spec(self, fullname, path=None, target=None):
        if fullname == 'flox':
            spec = importlib.machinery.PathFinder.find_spec(fullname, path)
            if spec and spec.loader:
                spec.loader = _FloxLoader(spec.loader)
            return spec
        return None

class _FloxLoader(importlib.abc.Loader):
    def __init__(self, orig_loader):
        self.orig_loader = orig_loader

    def create_module(self, spec):
        return self.orig_loader.create_module(spec)

    def exec_module(self, module):
        try:
            self.orig_loader.exec_module(module)
        except FileNotFoundError as e:
            if 'Unable to locate Launcher directory' in str(e):
                cwd = Path().cwd()
                user_dir = cwd.parent
                app_dir = user_dir
                module.USER_DIR = user_dir
                module.APP_DIR = app_dir
                module.API = 'Flow.Launcher'
                module.APP_ICONS = app_dir.joinpath('Images')
                module.ICON_APP = app_dir.joinpath('app.png')
                module.ICON_APP_ERROR = app_dir.joinpath('Images', 'app_error.png')
                module.ICON_BROWSER = app_dir.joinpath('Images', 'browser.png')
                module.ICON_CALCULATOR = app_dir.joinpath('Images', 'calculator.png')
                module.ICON_CANCEL = app_dir.joinpath('Images', 'cancel.png')
                module.ICON_CLOSE = app_dir.joinpath('Images', 'close.png')
                module.ICON_CMD = app_dir.joinpath('Images', 'cmd.png')
                module.ICON_COLOR = app_dir.joinpath('color.png')
                module.ICON_CONTROL_PANEL = app_dir.joinpath('ControlPanel.png')
                module.ICON_COPY = app_dir.joinpath('copy.png')
                module.ICON_DELETE_FILE_FOLDER = app_dir.joinpath('deletefilefolder.png')
                module.ICON_DISABLE = app_dir.joinpath('disable.png')
                module.ICON_DOWN = app_dir.joinpath('down.png')
                module.ICON_EXE = app_dir.joinpath('exe.png')
                module.ICON_FILE = app_dir.joinpath('file.png')
                module.ICON_FIND = app_dir.joinpath('find.png')
                module.ICON_FOLDER = app_dir.joinpath('folder.png')
                module.ICON_HISTORY = app_dir.joinpath('history.png')
                module.ICON_IMAGE = app_dir.joinpath('image.png')
                module.ICON_LOCK = app_dir.joinpath('lock.png')
                module.ICON_LOGOFF = app_dir.joinpath('logoff.png')
                module.ICON_NEW_FOLDER = app_dir.joinpath('newfolder.png')
                module.ICON_OPEN = app_dir.joinpath('open.png')
                module.ICON_PAINT = app_dir.joinpath('paint.png')
                module.ICON_PLUGIN = app_dir.joinpath('plugin.png')
                module.ICON_PROGRAM = app_dir.joinpath('program.png')
                module.ICON_RECYCLE_BIN = app_dir.joinpath('recyclebin.png')
                module.ICON_RESTART = app_dir.joinpath('restart.png')
                module.ICON_SEARCH = app_dir.joinpath('search.png')
                module.ICON_SETTINGS = app_dir.joinpath('settings.png')
                module.ICON_SHUTDOWN = app_dir.joinpath('shutdown.png')
                module.ICON_SLEEP = app_dir.joinpath('sleep.png')
                module.ICON_SNOOZE = app_dir.joinpath('snooze.png')
                module.ICON_SYS_SETTINGS = app_dir.joinpath('settings.png')
                module.ICON_TASK_MANAGER = app_dir.joinpath('taskmanager.png')
                module.ICON_UP = app_dir.joinpath('up.png')
                module.ICON_UPDATE = app_dir.joinpath('update.png')
                module.ICON_URL = app_dir.joinpath('url.png')
                module.ICON_USER = app_dir.joinpath('user.png')
                module.ICON_WARNING = app_dir.joinpath('warning.png')
                module.ICON_WEB_SEARCH = app_dir.joinpath('web_search.png')
                module.ICON_WORK = app_dir.joinpath('work.png')
            else:
                raise

sys.meta_path.insert(0, _FloxMetaFinder())
";
            File.WriteAllText(siteCustomizePath, code);
        }
        catch { }
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
