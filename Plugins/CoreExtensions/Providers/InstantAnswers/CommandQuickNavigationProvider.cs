using System.IO;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.InstantAnswers;

public sealed class CommandQuickNavigationProvider : IQuickNavigationProvider
{
    private readonly Action<string> _prompt;
    private readonly Func<string, bool> _directoryExists;
    public CommandQuickNavigationProvider() : this(Prompt, Directory.Exists) { }
    internal CommandQuickNavigationProvider(Action<string> prompt, Func<string, bool> directoryExists)
    {
        _prompt = prompt;
        _directoryExists = directoryExists;
    }
    public string GroupName => TranslationService.Get("Command_Name");
    public bool CanProvide(ISearchResult result) => CommandSettings.ShowInQuickNav;
    public IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu)
    {
        if (hMenu != IntPtr.Zero || !CanProvide(result)) yield break;
        var directory = result.ContextDirectory;
        yield return new DynamicMenuItem
        {
            Text = TranslationService.Get("Command_RunHere"),
            IsDisabled = string.IsNullOrWhiteSpace(directory) || !_directoryExists(directory),
            OnExecute = () => _prompt(directory)
        };
    }

    internal static void Prompt(string directory)
        => Prompt(directory, CommandSettings.Run);

    internal static void Prompt(string directory, Action<string, string, bool, bool, string?> run)
    {
        var shell = CommandSettings.ShellField();
        shell.DefaultValue = CommandSettings.Shell;
        var values = PluginPromptService.Prompt(TranslationService.Get("Command_RunHere") + " — " + directory,
            new PluginConfigField[]
            {
                new() { Key = "Command", LabelKey = "Command_Input", DescriptionKey = "Command_InputDesc", FieldType = ConfigFieldType.Text, DefaultValue = "" },
                shell,
                new() { Key = "Admin", LabelKey = "Command_RunAsAdmin", FieldType = ConfigFieldType.Boolean, DefaultValue = false }
            });
        if (values == null) return;
        var command = values.GetValueOrDefault("Command") as string ?? "";
        var selectedShell = values.GetValueOrDefault("CommandShell") as string ?? CommandSettings.Shell;
        var admin = values.GetValueOrDefault("Admin") is true;
        var trimmed = command.Trim();
        if (trimmed.StartsWith('#') || trimmed.StartsWith('$'))
        {
            admin = trimmed[0] == '#';
            command = trimmed[1..].Trim();
        }
        run(command, selectedShell, admin, true, directory);
    }

    public void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd) { }
    public void ClearSession() { }
}
