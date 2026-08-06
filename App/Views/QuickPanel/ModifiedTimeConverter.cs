using System.Globalization;
using System.Windows.Data;

namespace Lertaro.App.Views.QuickPanel;

/// <summary>Renders a modified time the way Recent Files does: absolute, then the interval.</summary>
/// <remarks>
/// A converter rather than a value written onto the item once, which is what this used to be. That
/// wrote at load time, and AppSearchResult.DateModified answers MinValue for anything the index does
/// not know yet while it fetches in the background: the write saw the placeholder, and nothing wrote
/// again when the real value arrived. Network paths hit that constantly, which is where the timestamps
/// went missing. Bound, the row simply updates when the property does.
///
/// MinValue renders as nothing rather than as a date in year one.
/// </remarks>
public sealed class ModifiedTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateTime modified || modified == DateTime.MinValue) return string.Empty;

        // Absolute first, interval in brackets. The interval alone answers "how stale is this" at a
        // glance but never "which day was that"; the absolute alone is the reverse. The absolute half is
        // formatted by the current culture rather than a fixed pattern, so a machine set to a language
        // that writes the date the other way round gets its own order.
        var relative = RelativeTime.Describe(modified);
        var absolute = modified.ToString("g", CultureInfo.CurrentCulture);
        return string.IsNullOrEmpty(relative) ? absolute : $"{absolute} ({relative})";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
