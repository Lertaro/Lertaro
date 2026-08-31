using Lertaro.App.Helpers;
using Lertaro.App.Views.Controls.Dialogs;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Services.AppWindow;

// Keeps quick-launch item editing out of AppWindowManager so the manager remains below the repo's
// per-file size limit. The editor deliberately uses PluginFieldPromptWindow, the shared prompt used
// by plugin settings and the existing "add current folder" flow.
internal static class QuickLaunchItemEditor
{
    public static void Show(string path)
    {
        if (System.Windows.Application.Current == null) return;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var settings = UserSettings.Load();
            var item = settings.QuickLaunch.Items.FirstOrDefault(candidate =>
                string.Equals(
                    FavoritePathResolver.NormalizeForComparison(candidate.Path),
                    FavoritePathResolver.NormalizeForComparison(path),
                    StringComparison.OrdinalIgnoreCase));
            if (item == null) return;

            var fields = new List<PluginConfigField>
            {
                new()
                {
                    Key = "Name",
                    LabelKey = "QuickLaunch_FieldName",
                    FieldType = ConfigFieldType.Text,
                    DefaultValue = item.Name
                },
                new()
                {
                    Key = "Path",
                    LabelKey = "QuickLaunch_FieldPath",
                    FieldType = ConfigFieldType.Text,
                    DefaultValue = item.Path
                }
            };
            var values = PluginFieldPromptWindow.ShowPrompt(
                TranslationManager.Instance["QuickLaunch_Edit"],
                fields,
                new Dictionary<string, object?> { ["Name"] = item.Name, ["Path"] = item.Path });
            if (values == null) return;

            var editedName = values.TryGetValue("Name", out var nameValue) ? nameValue?.ToString() ?? string.Empty : item.Name;
            var editedPath = values.TryGetValue("Path", out var pathValue) ? pathValue?.ToString() ?? string.Empty : item.Path;
            editedPath = editedPath.Trim().Trim('"');
            if (!FavoritePathResolver.IsPathAvailable(editedPath)) return;
            if (settings.QuickLaunch.Items.Any(candidate => !ReferenceEquals(candidate, item)
                    && string.Equals(FavoritePathResolver.NormalizeForComparison(candidate.Path),
                        FavoritePathResolver.NormalizeForComparison(editedPath), StringComparison.OrdinalIgnoreCase)))
                return;

            item.Name = editedName.Trim();
            item.Path = editedPath;
            settings.Save();

            foreach (var window in System.Windows.Application.Current.Windows.OfType<QuickSearchWindow>())
            {
                if (window.DataContext is ViewModels.Search.QuickSearchViewModel viewModel)
                    viewModel.RefreshLaunchItems();
            }
        });
    }
}
