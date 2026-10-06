using System.Globalization;
using System.IO;
using Lertaro.App.Views.Controls.Dialogs;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.App.ViewModels.Settings;
using MessageBox = Lertaro.App.Views.Controls.Dialogs.CustomMessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace Lertaro.App.Services;

/// <summary>
/// Behind the About page's Config Management card: enumerating the .bak.N rotation of
/// user-settings.json for the restore picker, packaging settings for export, and the
/// interactive export/import/restore flows themselves. The flows live here rather than in
/// AboutSettingsPage to keep that page under the repo's per-file line limit -- the same
/// Services-class-shows-localized-message-boxes split ExplorerLocateHelper and FileExecutor already
/// embody. Actual replacement of the settings (import/restore) goes through
/// SettingsBackup in Core before plugin initialization; the background service is never stopped.
/// </summary>
internal static class UserConfigBackups
{
    internal sealed record RestoreChoice(string Display, string Path);

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

    internal static IReadOnlyList<RestoreChoice> BuildRestoreChoices(
        IReadOnlyList<(string Path, DateTime ModifiedTime)> backups)
    {
        var choices = new List<RestoreChoice>(backups.Count);
        var displayCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (path, modifiedTime) in backups)
        {
            var baseDisplay = modifiedTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            displayCounts.TryGetValue(baseDisplay, out var count);
            count++;
            displayCounts[baseDisplay] = count;
            var display = count == 1 ? baseDisplay : $"{baseDisplay} ({count})";
            choices.Add(new RestoreChoice(display, path));
        }

        return choices;
    }

    /// <summary>Packages persisted user settings into <paramref name="targetFolder"/>. Returns the
    /// destination path, or null when there is no settings file yet (fresh install).</summary>
    internal static string? Export(string targetFolder) => Export(UserSettings.SettingsPath, targetFolder);

    // Noninteractive path for callers that have already stopped all writers; the UI uses a restart.
    internal static string? Export(string settingsPath, string targetFolder)
    {
        try { using var input = File.OpenRead(settingsPath); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        var destination = Path.Combine(targetFolder, SettingsBackup.FileName);
        SettingsBackup.Export(Path.GetDirectoryName(settingsPath)!, destination, typeof(App).Assembly.GetName().Version?.ToString() ?? "");
        return destination;
    }

    // ---- Interactive flows behind the About page's three Config Management buttons ----
    // Each flow is fully try-wrapped so an unexpected error surfaces through the shared failure
    // message box instead of escaping the page's async void click handler.

    private static bool _transferRunning;

    /// <summary>Save personal edits, flush plugin writers, then take the snapshot on restart.</summary>
    internal static async Task RunExportFlowAsync(SettingsViewModel? settings = null)
    {
        if (_transferRunning) return;
        _transferRunning = true;
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = SettingsBackup.FileName, DefaultExt = ".zip", Filter = "ZIP (*.zip)|*.zip" };
            if (dialog.ShowDialog() != true) return;
            var options = PromptPluginFiles();
            if (options == null || !Confirm(TranslationManager.Instance["About_ConfigExportConfirm"])) return;
            if (settings != null && !await settings.ApplyAsync(personalOnly: true)) return;
            var directory = SettingsTransferRestart.CreateRequestDirectory();
            await SettingsTransferRestart.RestartAsync(directory, new(true, dialog.FileName, options.Value,
                TranslationManager.Instance["About_ConfigExportSuccess"]));
        }
        catch (Exception ex)
        {
            ShowActionFailed(ex);
        }
        finally { _transferRunning = false; }
    }

    /// <summary>Stages an external ZIP or legacy JSON and restarts to apply it.</summary>
    internal static async Task RunImportFlowAsync()
    {
        if (_transferRunning) return;
        _transferRunning = true;
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = $"{TranslationManager.Instance["About_ConfigFileFilter"]} (*.zip;*.json)|*.zip;*.json|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog() != true) return;

            await ApplySourceAsync(dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowActionFailed(ex);
        }
        finally { _transferRunning = false; }
    }

    /// <summary>Offers the on-disk .bak.N backups newest first and restarts to restore the selection.</summary>
    internal static async Task RunRestoreFlowAsync()
    {
        if (_transferRunning) return;
        _transferRunning = true;
        try
        {
            var backups = Enumerate(Logger.UserDataDir);
            if (backups.Count == 0)
            {
                ShowInfo(TranslationManager.Instance["About_ConfigRestoreNone"]);
                return;
            }

            var choices = BuildRestoreChoices(backups);
            const string fieldKey = "BackupPath";
            var field = new PluginConfigField
            {
                Key = fieldKey,
                LabelKey = TranslationManager.Instance["About_ConfigRestoreHint"],
                FieldType = ConfigFieldType.Choice,
                Choices = choices.Select(choice => choice.Display).ToList(),
                DefaultValue = choices[0].Display
            };
            var values = PluginFieldPromptWindow.ShowPrompt(
                TranslationManager.Instance["About_ConfigRestoreTitle"],
                new[] { field },
                initialValues: null);

            if (values?.TryGetValue(fieldKey, out var selectedValue) == true && selectedValue is string selectedDisplay)
            {
                var selected = choices.FirstOrDefault(choice => choice.Display == selectedDisplay);
                if (selected is not null)
                    await ApplySourceAsync(selected.Path);
            }
        }
        catch (Exception ex)
        {
            ShowActionFailed(ex);
        }
        finally { _transferRunning = false; }
    }

    // Validate/stage before confirmation. The live tree changes only after every old plugin is gone.
    private static async Task ApplySourceAsync(string sourcePath)
    {
        var directory = SettingsTransferRestart.CreateRequestDirectory();
        var preserveForRestart = false;
        try
        {
            // Staging code is never executed. Consent to install any included plugin files comes below.
            var manifest = await Task.Run(() => SettingsBackup.Prepare(sourcePath, Path.Combine(directory, "payload"), allowPluginFiles: true));
            var details = string.Format(TranslationManager.Instance["About_ConfigImportConfirm"], sourcePath, manifest.Files.Count);
            if (manifest.IsLegacyJson) details += "\n\n" + TranslationManager.Instance["About_ConfigLegacyOnly"];
            if (manifest.IncludesPluginFiles) details += "\n\n" + TranslationManager.Instance["About_ConfigPluginFilesWarning"];
            if (!Confirm(details)) return;
            preserveForRestart = true;
            await SettingsTransferRestart.RestartAsync(directory, new(false, null, manifest.IncludesPluginFiles,
                TranslationManager.Instance["About_ConfigImportSuccess"]));
        }
        finally
        {
            if (!preserveForRestart)
            {
                try { using var staging = new SettingsBackupPaths(directory); staging.ClearFiles("payload"); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Logger.Log($"[SettingsTransfer] Could not clean staging: {ex.Message}", LogLevel.Warn); }
            }
        }
    }

    private static bool? PromptPluginFiles()
    {
        const string key = "IncludePluginFiles";
        var values = PluginFieldPromptWindow.ShowPrompt(TranslationManager.Instance["About_ConfigSection"],
            [new PluginConfigField { Key = key, LabelKey = TranslationManager.Instance["About_ConfigIncludePluginFiles"],
                FieldType = ConfigFieldType.Boolean, DefaultValue = false }], initialValues: null);
        return values == null ? null : values.TryGetValue(key, out var value) && value is true;
    }

    // Localized message boxes; Service_Error captions failures, About_ConfigSection captions feature
    // messaging -- the same convention AboutSettingsPage's own handlers use.
    private static void ShowInfo(string text) => MessageBox.Show(text, TranslationManager.Instance["About_ConfigSection"], MessageBoxButton.OK, MessageBoxImage.Information);
    private static void ShowError(string text) => MessageBox.Show(text, TranslationManager.Instance["Service_Error"], MessageBoxButton.OK, MessageBoxImage.Error);
    private static void ShowActionFailed(Exception ex) => ShowError(string.Format(TranslationManager.Instance["About_ConfigActionFailed"], ex.Message));
    private static bool Confirm(string text) => MessageBox.Show(text, TranslationManager.Instance["About_ConfigSection"], MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
}
