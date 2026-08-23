using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

/// <summary>
/// Downloads and extracts the official embeddable Python package for Flow plugins.
/// Configures import site and sitecustomize.py hook for seamless execution in FlowData.
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
                _ = Task.Run(() => FlowPipManager.EnsurePipInstalledAsync(pyExe));
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
            const string code = @"import sys, os, importlib.abc, importlib.machinery, re, json
from pathlib import Path

class _FloxMetaFinder(importlib.abc.MetaPathFinder):
    def find_spec(self, fullname, path=None, target=None):
        if fullname in ('flox', 'flox.__init__'):
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

            code_str = re.sub(
                r'launcher_dir\s*=\s*None\s+path\s*=\s*CURRENT_WORKING_DIR[\s\S]*?path\s*=\s*path\.parent',
                ""launcher_name = 'FlowLauncher'\nAPI = FLOW_API\n_curr = Path().cwd()\n_p = _curr.parent\nUSER_DIR = _p.parent if _p.name.lower() == 'plugins' else _p\nAPP_DIR = USER_DIR"",
                code_str
            )
            code_str = re.sub(r'raise FileNotFoundError\(.*Launcher directory.*\)', 'pass', code_str)
            code_str = code_str.replace(
                'return os.path.dirname(os.path.dirname(self.plugindir))',
                'p1 = os.path.dirname(self.plugindir)\n        p2 = os.path.dirname(p1)\n        return p2 if os.path.basename(p1).lower() == ""plugins"" else p1'
            )
            code_str = re.sub(
                r'def app_settings\(self\):[\s\S]*?return json\.load\(f\)',
                ""def app_settings(self):\n        try:\n            with open(os.path.join(self.appdata, 'Settings', 'Settings.json'), 'r', encoding='utf-8') as f:\n                return json.load(f)\n        except Exception:\n            return {'PluginSettings': {'Plugins': {}, 'PythonDirectory': sys.prefix}, 'QuerySearchPrecision': 'Regular'}"",
                code_str
            )
            code_str = code_str.replace(""'Settings', 'Plugins'"", ""'Settings'"")
            code_str = code_str.replace('""Settings"", ""Plugins""', '""Settings""')
            code_str = code_str.replace('os.mkdir(os.path.dirname(self.settings_path))', 'os.makedirs(os.path.dirname(self.settings_path), exist_ok=True)')

            compiled = compile(code_str, module.__file__, 'exec')
            exec(compiled, module.__dict__)
        except Exception:
            self.orig_loader.exec_module(module)

sys.meta_path.insert(0, _FloxMetaFinder())

import builtins
_orig_stat = os.stat
_orig_open = builtins.open
_orig_exists = os.path.exists

def _remap_settings_path(path_obj):
    try:
        s = str(path_obj)
        if 'Settings' in s and ('Settings/Plugins/' in s.replace('\\', '/') or 'Settings\\Plugins\\' in s):
            alt = s.replace('Settings\\Plugins\\', 'Settings\\').replace('Settings/Plugins/', 'Settings/')
            if _orig_exists(alt):
                return alt
    except Exception:
        pass
    return path_obj

def _hooked_stat(path, *args, **kwargs):
    try:
        return _orig_stat(path, *args, **kwargs)
    except (FileNotFoundError, OSError):
        remapped = _remap_settings_path(path)
        if remapped != path:
            return _orig_stat(remapped, *args, **kwargs)
        raise

def _hooked_exists(path):
    if _orig_exists(path):
        return True
    remapped = _remap_settings_path(path)
    return _orig_exists(remapped)

def _hooked_open(file, *args, **kwargs):
    try:
        return _orig_open(file, *args, **kwargs)
    except (FileNotFoundError, OSError):
        remapped = _remap_settings_path(file)
        if remapped != file:
            return _orig_open(remapped, *args, **kwargs)
        raise

os.stat = _hooked_stat
os.path.exists = _hooked_exists
builtins.open = _hooked_open

import subprocess
try:
    if sys.platform == 'win32' and hasattr(subprocess, 'Popen'):
        _orig_popen_init = subprocess.Popen.__init__
        def _silent_popen_init(self, *args, **kwargs):
            kwargs['creationflags'] = kwargs.get('creationflags', 0) | 0x08000000
            if 'startupinfo' not in kwargs:
                si = subprocess.STARTUPINFO()
                si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
                si.wShowWindow = 0
                kwargs['startupinfo'] = si
            return _orig_popen_init(self, *args, **kwargs)
        subprocess.Popen.__init__ = _silent_popen_init
except Exception:
    pass
";
            File.WriteAllText(siteCustomizePath, code);
        }
        catch { }
    }

    private static string GetDownloadUrl()
    {
        var archSuffix = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "embed-arm64"
            : "embed-amd64";

        return $"https://www.python.org/ftp/python/3.12.7/python-3.12.7-{archSuffix}.zip";
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

        var candidates = new[] { "python.exe", "pythonw.exe" };
        foreach (var name in candidates)
        {
            var full = Path.Combine(dir, name);
            if (File.Exists(full))
                return full;
        }

        return null;
    }
}
