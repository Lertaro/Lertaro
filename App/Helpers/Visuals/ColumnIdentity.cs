using System.Windows;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// Stable per-column identifier for the full window's results GridView. GridViewColumn is a
/// DependencyObject, not a FrameworkElement, so it has no built-in Tag to stash an id on -- this
/// attached property fills that gap so sort state can be keyed by a stable id ("Name"/"Path"/
/// "DateModified" for the built-ins, a plugin's own ResultColumnDefinition.ColumnId for the rest)
/// instead of the column's displayed (translated, and therefore language-dependent) header text.
/// </summary>
public static class ColumnIdentity
{
    public static readonly DependencyProperty IdProperty = DependencyProperty.RegisterAttached(
        "Id", typeof(string), typeof(ColumnIdentity), new PropertyMetadata(string.Empty));

    public static void SetId(DependencyObject d, string value) => d.SetValue(IdProperty, value);
    public static string GetId(DependencyObject d) => (string)d.GetValue(IdProperty);
}
