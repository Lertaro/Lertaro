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
            code_bytes = self.orig_loader.get_data(module.__file__)
            code_str = code_bytes.decode('utf-8').replace('\r\n', '\n')
            
            old_block = ""if SCOOP_FLOW_LAUNCHER_DIR_NAME.lower() in str(path).lower():\n    launcher_name = SCOOP_FLOW_LAUNCHER_DIR_NAME\n    API = FLOW_API\nelif FLOW_LAUNCHER_DIR_NAME.lower() in str(path).lower():\n    launcher_name = FLOW_LAUNCHER_DIR_NAME\n    API = FLOW_API\nelif WOX_DIR_NAME.lower() in str(path).lower():\n    launcher_name = WOX_DIR_NAME\n    API = WOX_API\nelse:\n    raise FileNotFoundError(LAUNCHER_NOT_FOUND_MSG)\n\nwhile True:\n    if len(path.parts) == 1:\n        raise FileNotFoundError(LAUNCHER_NOT_FOUND_MSG)\n    if path.joinpath('Settings').exists():\n        USER_DIR = path\n        if USER_DIR.name == 'UserData':\n            APP_DIR = USER_DIR.parent\n        elif str(CURRENT_WORKING_DIR).startswith(str(APPDATA)):\n            APP_DIR = LOCALAPPDATA.joinpath(launcher_name)\n        else:\n            raise FileNotFoundError(LAUNCHER_NOT_FOUND_MSG)\n        break\n\n    path = path.parent""
            new_block = ""launcher_name = 'FlowLauncher'\nAPI = FLOW_API\nUSER_DIR = Path().cwd().parent\nAPP_DIR = USER_DIR""
            
            if (old_block in code_str):
                code_str = code_str.replace(old_block, new_block)
            else:
                code_str = code_str.replace('raise FileNotFoundError(LAUNCHER_NOT_FOUND_MSG)', 'pass')
                
            old_appdata = 'return os.path.dirname(os.path.dirname(self.plugindir))'
            new_appdata = 'p1 = os.path.dirname(self.plugindir)\n        return p1 if os.path.exists(os.path.join(p1, \'Settings\')) else os.path.dirname(p1)'
            code_str = code_str.replace(old_appdata, new_appdata)

            old_settings = ""with open(os.path.join(self.appdata, 'Settings', 'Settings.json'), 'r', encoding='utf-8') as f:\n            return json.load(f)""
            new_settings = ""try:\n            with open(os.path.join(self.appdata, 'Settings', 'Settings.json'), 'r', encoding='utf-8') as f:\n                return json.load(f)\n        except Exception:\n            return {'PluginSettings': {'Plugins': {}}, 'QuerySearchPrecision': 'Regular'}""
            code_str = code_str.replace(old_settings, new_settings)
            code_str = code_str.replace('os.mkdir(os.path.dirname(self.settings_path))', 'os.makedirs(os.path.dirname(self.settings_path), exist_ok=True)')

            compiled = compile(code_str, module.__file__, 'exec')
            exec(compiled, module.__dict__)
        except Exception:
            self.orig_loader.exec_module(module)

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
