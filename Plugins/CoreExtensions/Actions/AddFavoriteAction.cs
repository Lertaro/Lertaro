using System.Windows.Media;

using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Models;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Actions;

public sealed class AddFavoriteAction : ISearchResultAction
{
    private const string NameFieldKey = "FavoriteName";

    public string GroupName => TranslationService.Get("Action_BuiltinGroup");
    public string DisplayName => TranslationService.Get("Action_AddFavorite");
    public string Description => TranslationService.Get("Action_AddFavorite_Desc");

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M12 17.27 18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z",
        "TextPrimary");

    public bool CanExecute(IReadOnlyList<ISearchResult> results) =>
        results.Count == 1
        && PathExistenceCache.ExistsResult(results[0])
        && !FavoritesService.IsFavorite(results[0].FullPath);

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view)
    {
        if (!CanExecute(results))
            return;

        var result = results[0];
        var values = PluginPromptService.Prompt(
            TranslationService.Get("Action_AddFavorite_DialogTitle"),
            new[]
            {
                new PluginConfigField
                {
                    Key = NameFieldKey,
                    LabelKey = "Action_AddFavorite_NameLabel",
                    FieldType = ConfigFieldType.Text,
                    DefaultValue = result.Name,
                    RequireNonEmpty = true
                }
            });

        if (values == null || !values.TryGetValue(NameFieldKey, out var value) || value is not string name)
            return;

        FavoritesService.TryAddFavorite(new FavoriteItem { Name = name.Trim(), Path = result.FullPath });
    }
}
