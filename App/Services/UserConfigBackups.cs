using System.IO;
using Lertaro.App.Views.Controls.Dialogs;
using Lertaro.App.Views.Settings;
using Lertaro.Core;
using MessageBox = Lertaro.App.Views.Controls.Dialogs.CustomMessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using WindowStartupLocation = System.Windows.WindowStartupLocation;
using Application = System.Windows.Application;

namespace Lertaro.App.Services;

/// <summary>
/// Behind the About page's Config Management card: enumerating the .bak.N rotation of
/// user-settings.json for the restore picker, copying the live settings out for export, and the
/// interactive export/import/restore flows themselves. The flows live here rather than in
/// AboutSettingsPage to keep that page under the repo's per-file line limit -- the same
/// Services-class-shows-localized-message-boxes split ExplorerLocateHelper and FileExecutor already
/// embody. Actual replacement of the settings (import/restore) goes through
/// UserSettings.RestoreFrom in Core; the background service is never touched.
/// </summary>
internal static class UserConfigBackups
{
    /// <summary>Backups of user-settings.json (its .bak.N rotation), newest first. The rotation count
    /// is whatever the directory happens to hold: zero entries is normal for a fresh install.</summary>
    internal static IReadOnlyList<(string Path, DateTime ModifiedTime)> Enumerate(string dataDirectory)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(dataDirectory, "user-settings.json.bak.*", SearchOption.TopDirectoryOnly);
        }
        catch (DirectoryNotFoundException)
        {
            // The directory always exists in practice (Logger.UserDataDir), but a caller may hand us a
            // path that has not been created yet -- that is an empty rotation, not an error.
            return Array.Empty<(string Path, DateTime ModifiedTime)>();
        }

        return files
            .Select(f => (Path: f, ModifiedTime: File.GetLastWriteTime(f)))
            .OrderByDescending(b => b.ModifiedTime)
            .ToList();
    }

    /// <summary>Copies the live user settings into <paramref name="targetFolder"/>. Returns the
    /// destination path, or null when there is no settings file yet (fresh install).</summary>
    internal static string? Export(string targetFolder) => Export(UserSettings.SettingsPath, targetFolder);

    // Path-parameterized overload so tests run against temp directories; the UI always calls the
    // one-argument overload above.
    internal static string? Export(string settingsPath, string targetFolder)
    {
        if (!File.Exists(settingsPath)) return null;
        var destination = Path.Combine(targetFolder, Path.GetFileName(settingsPath));
        File.Copy(settingsPath, destination, overwrite: true);
        return destination;
    }

    // ---- Interactive flows behind the About page's three Config Management buttons ----
    // Each flow is fully try-wrapped so an unexpected error surfaces through the shared failure
    // message box instead of escaping the page's async void click handler.

    /// <summary>Picks a folder and copies user-settings.json into it. Never exits the app.</summary>
    internal static async Task RunExportFlowAsync()
    {
        try
        {
            // Existence is checked first so the folder picker is not shown for nothing.
            if (!File.Exists(UserSettings.SettingsPath))
            {
                ShowMissingSettings();
                return;
            }

            var dialog = new Microsoft.Win32.OpenFolderDialog();
            if (dialog.ShowDialog() != true) return;

            var destination = Path.Combine(dialog.FolderName, Path.GetFileName(UserSettings.SettingsPath));
            if (File.Exists(destination) && !Confirm(string.Format(TranslationManager.Instance["About_ConfigExportOverwrite"], destination)))
                return;

            var exported = await Task.Run(() => Export(dialog.FolderName));
            if (exported == null)
            {
                // The settings file vanished while the folder picker was open.
                ShowMissingSettings();
                return;
            }

            ShowInfo(string.Format(TranslationManager.Instance["About_ConfigExportSuccess"], exported));
        }
        catch (Exception ex)
        {
            ShowActionFailed(ex);
        }
    }

    /// <summary>Picks an external JSON and replaces the live settings with it, then exits.</summary>
    internal static async Task RunImportFlowAsync()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = $"{TranslationManager.Instance["About_ConfigFileFilter"]} (*.json)|*.json|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog() != true) return;

            if (!Confirm(string.Format(TranslationManager.Instance["About_ConfigImportConfirm"], dialog.FileName)))
                return;

            await ApplySourceAsync(dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowActionFailed(ex);
        }
    }

    /// <summary>Offers the on-disk .bak.N backups newest first and restores the picked one, then exits.</summary>
    internal static async Task RunRestoreFlowAsync()
    {
        try
        {
            var backups = Enumerate(Logger.UserDataDir);
            if (backups.Count == 0)
            {
                ShowInfo(TranslationManager.Instance["About_ConfigRestoreNone"]);
                return;
            }

            var dialog = new ConfigRestoreWindow(backups);
            dialog.Owner = OwnedDialog.ResolveOwner(dialog);
            dialog.WindowStartupLocation = dialog.Owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            // Not dialog.ShowDialog(): see OwnedDialog.ShowModal -- the same modal-showing path every
            // custom dialog in the app takes, so an owner closing underneath cannot freeze the app.
            OwnedDialog.ShowModal(dialog);

            if (dialog.SelectedBackupPath is string selected)
                await ApplySourceAsync(selected);
        }
        catch (Exception ex)
        {
            ShowActionFailed(ex);
        }
    }

    // Shared tail of Import and Restore: swap the settings file via Core's RestoreFrom -- it
    // validates the source and rotates the current file into the backup chain first -- then prompt
    // and exit so a restart picks everything up. Deliberately NOT TrayCleanExitHelper: the
    // background service must keep running.
    private static async Task ApplySourceAsync(string sourcePath)
    {
        try
        {
            await Task.Run(() => UserSettings.RestoreFrom(sourcePath));
        }
        catch (InvalidDataException)
        {
            ShowError(string.Format(TranslationManager.Instance["About_ConfigInvalidFile"], sourcePath));
            return;
        }

        PromptRestartAndExit();
    }

    private static void PromptRestartAndExit()
    {
        ShowInfo(TranslationManager.Instance["About_ConfigRestartPrompt"]);
        Application.Current.Shutdown();
    }

    // Localized message boxes; Service_Error captions failures, About_ConfigSection captions feature
    // messaging -- the same convention AboutSettingsPage's own handlers use.
    private static void ShowInfo(string text) => MessageBox.Show(text, TranslationManager.Instance["About_ConfigSection"], MessageBoxButton.OK, MessageBoxImage.Information);
    private static void ShowError(string text) => MessageBox.Show(text, TranslationManager.Instance["Service_Error"], MessageBoxButton.OK, MessageBoxImage.Error);
    private static void ShowMissingSettings() => MessageBox.Show(string.Format(TranslationManager.Instance["About_ConfigMissing"], UserSettings.SettingsPath), TranslationManager.Instance["Service_Error"], MessageBoxButton.OK, MessageBoxImage.Warning);
    private static void ShowActionFailed(Exception ex) => ShowError(string.Format(TranslationManager.Instance["About_ConfigActionFailed"], ex.Message));
    private static bool Confirm(string text) => MessageBox.Show(text, TranslationManager.Instance["About_ConfigSection"], MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
}
