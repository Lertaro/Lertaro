using System.Globalization;
using System.Windows.Data;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Converters;

/// <summary>
/// Renders a <see cref="SearchResultCategory"/> as the tab caption. Kept as a converter rather than a
/// wrapper ViewModel so the tab list can stay a plain array of the enum: the tabs have no state of their
/// own, the selection lives on the search view model.
/// </summary>
public sealed class CategoryDisplayNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is SearchResultCategory category
            ? TranslationManager.Instance[SearchExecutionViewModel.CategoryTranslationKey(category)]
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("Category captions are display-only.");
}
