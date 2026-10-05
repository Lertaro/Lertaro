using System.ComponentModel;
using Lertaro.PluginSdk;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.InstantAnswers;

internal static class CommandSettings
{
    internal const string PluginId = "Lertaro.Plugins.CoreExtensions";
    internal static string Shell => PluginSettingsService.GetSetting(PluginId, "CommandShell", "cmd");
    internal static bool UseCurrentDirectory => PluginSettingsService.GetSetting(PluginId, "CommandUseCurrentDirectory", false);
    internal static bool ShowInQuickNav => PluginSettingsService.GetSetting(PluginId, "CommandShowInQuickNav", false);

    internal static PluginConfigField ShellField() => new()
    {
        Key = "CommandShell", LabelKey = "Command_Config_Shell", FieldType = ConfigFieldType.Choice, DefaultValue = "cmd",
        ChoiceOptions = new()
        {
            new() { Value = "cmd", LabelKey = "Command_Shell_Cmd" },
            new() { Value = "powershell", LabelKey = "Command_Shell_WindowsPowerShell" },
            new() { Value = "pwsh", LabelKey = "Command_Shell_Pwsh" }
        }
    };

    internal static PluginConfigField Config() => new()
    {
        Key = "CommandGroup", LabelKey = "Command_Name", FieldType = ConfigFieldType.Group,
        SubFields = new()
        {
            ShellField(),
            new() { Key = "CommandUseCurrentDirectory", LabelKey = "Command_Config_CurrentDirectory", DescriptionKey = "Command_Config_CurrentDirectoryDesc", FieldType = ConfigFieldType.Boolean, DefaultValue = false },
            new() { Key = "CommandShowInQuickNav", LabelKey = "Command_Config_ShowInQuickNav", FieldType = ConfigFieldType.Boolean, DefaultValue = false }
        }
    };

    internal static void Run(string command, string shell, bool admin, bool useCurrentDirectory, string? contextDirectory)
    {
        try
        {
            var directory = ShellCommandLauncher.ResolveWorkingDirectory(null, useCurrentDirectory, contextDirectory);
            ShellCommandLauncher.Launch(command, shell, admin, directory);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { }
        catch (Exception ex)
        {
            Logger.Log($"[CoreExtensions] Command launch failed: {ex.Message}", LogLevel.Error);
            PluginNotificationService.Show(TranslationService.Get("Command_Name"), ex.Message);
        }
    }
}
