using System.IO;
using System.Windows.Media;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Shell.FileOperations;

namespace Lertaro.Plugins.CoreExtensions.Actions;

public sealed class RenameAction : ISearchResultAction
{
    private const string NameFieldKey = "RenameName";

    public string GroupName => TranslationService.Get("Action_BuiltinGroup");
    public string DisplayName => TranslationService.Get("Action_Rename");
    public string Description => TranslationService.Get("Action_Rename_Desc");

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04c.39-.39.39-1.03 0-1.42l-2.34-2.34a.9959.9959 0 0 0-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.82z",
        "TextPrimary");

    public bool CanExecute(IReadOnlyList<ISearchResult> results) =>
        results.Count == 1 && results[0] != null && PathExistenceCache.ExistsResult(results[0]);

    internal static (string Name, int SelectionStart, int SelectionLength) GetInitialEditorState(
        ISearchResult result)
    {
        var pathName = Path.GetFileName(Path.TrimEndingDirectorySeparator(result.FullPath));
        var name = string.IsNullOrEmpty(pathName) ? result.Name : pathName;
        var selectedName = result.IsDir ? name : Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrEmpty(selectedName)) selectedName = name;
        return (name, 0, selectedName.Length);
    }

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view)
    {
        if (!CanExecute(results)) return;

        var result = results[0];
        var initial = GetInitialEditorState(result);
        var values = PluginPromptService.Prompt(
            TranslationService.Get("Action_Rename_DialogTitle"),
            new[]
            {
                new PluginConfigField
                {
                    Key = NameFieldKey,
                    LabelKey = "Action_Rename_NameLabel",
                    FieldType = ConfigFieldType.Text,
                    DefaultValue = initial.Name,
                    RequireNonEmpty = true,
                    SelectionStart = initial.SelectionStart,
                    SelectionLength = initial.SelectionLength
                }
            });

        if (values == null || !values.TryGetValue(NameFieldKey, out var value) || value is not string name)
            return;

        ShellRenameHelper.RenameAsync(result.FullPath, name);
    }
}
