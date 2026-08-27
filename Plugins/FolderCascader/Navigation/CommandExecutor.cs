using System.IO;
using Lertaro.PluginSdk;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FolderCascader.Navigation;

public static class CommandExecutor
{
    public static void Execute(ISearchResult result, string path)
    {
        var targetDir = result.IsDir ? result.FullPath : Path.GetDirectoryName(result.FullPath);
        if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
        {
            targetDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (path == "powershell")
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    WorkingDirectory = targetDir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[FolderCascader] Failed to launch PowerShell: {ex.Message}", LogLevel.Error);
            }
        }
        else if (path == "cmd")
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    WorkingDirectory = targetDir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[FolderCascader] Failed to launch Command Prompt: {ex.Message}", LogLevel.Error);
            }
        }
        else if (path == "options")
        {
            try
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var settingsWindowType = System.Reflection.Assembly.GetExecutingAssembly().GetType("Lertaro.App.Views.Settings.SettingsWindow") ?? (System.Reflection.Assembly.GetEntryAssembly()?.GetType("Lertaro.App.Views.Settings.SettingsWindow"));
                        if (settingsWindowType != null)
                        {
                            var win = Activator.CreateInstance(settingsWindowType) as System.Windows.Window;
                            win?.Show();
                        }
                    }
                    catch { }
                }));
            }
            catch (Exception ex)
            {
                Logger.Log($"[FolderCascader] Failed to launch Options: {ex.Message}", LogLevel.Error);
            }
        }
        else
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[FolderCascader] Failed to execute {path}: {ex.Message}", LogLevel.Error);
            }
        }
    }

    // Appends a new Folders entry for the currently active directory, nested under whichever level the
    // "Add Current Folder" item was clicked from (subMenu is "" for root). Writes back through the SDK's
    // SetSetting -- unlike every other command here, this doesn't launch anything, it edits this
    // plugin's own configuration at runtime. Wired directly as a DynamicMenuItem.OnExecute delegate
    // (see MenuBuilder.AppendAddCurrentFolderItem) rather than through the CommandId/Execute(path)
    // mechanism above: the host resolves any allocated CommandId straight to its stored string and
    // hands that to NavigateOrOpen as a literal path *before* Provider.ExecuteCommand/this class ever
    // gets a look at it, so a CommandId here would have opened "the sentinel string" via the shell
    // instead of running this.
    internal static void AddCurrentFolder(string folderPath, string subMenu, string name = "")
    {
        // Use the same resolver as configured folder entries so environment variables and Shell
        // namespace paths are validated without replacing the original value stored in settings.
        if (!PathAvailability.IsFolderAvailable(folderPath))
            return;

        var folders = PluginSettingsService.GetSetting(
            "Lertaro.Plugins.FolderCascader",
            "Folders",
            new List<FolderCascaderPlugin.FolderConfigItem>());
        folders ??= new List<FolderCascaderPlugin.FolderConfigItem>();

        folders.Add(new FolderCascaderPlugin.FolderConfigItem { Name = name, Path = folderPath, SubMenu = subMenu });
        PluginSettingsService.SetSetting("Lertaro.Plugins.FolderCascader", "Folders", folders);
    }
}
