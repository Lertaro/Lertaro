using System.Globalization;
using System.Windows.Data;

namespace Lertaro.App.Views.QuickPanel;

/// <summary>Formats a quick panel list row's display text: Name (RelativeParentDir).</summary>
public sealed class QuickPanelDisplayNameConverter : IMultiValueConverter, IValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length > 0 && values[0] is AppSearchResult item)
        {
            var groupFolder = values.Length > 1 ? values[1] as string : null;
            var relativeDir = QuickPanelPathHelper.GetRelativeDirectory(item.ParentDir, groupFolder);
            return string.IsNullOrEmpty(relativeDir) ? item.Name : $"{item.Name} ({relativeDir})";
        }
        return string.Empty;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is AppSearchResult item)
        {
            return item.Name;
        }
        return value ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns the relative parent directory of an item against its containing group's folder.</summary>
public sealed class QuickPanelRelativeDirectoryConverter : IMultiValueConverter, IValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length > 0 && values[0] is AppSearchResult item)
        {
            var groupFolder = values.Length > 1 ? values[1] as string : null;
            var relativeDir = QuickPanelPathHelper.GetRelativeDirectory(item.ParentDir, groupFolder);
            return string.IsNullOrEmpty(relativeDir) ? null : relativeDir;
        }
        return null;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is AppSearchResult item)
        {
            return string.IsNullOrEmpty(item.ParentDir) ? null : item.ParentDir;
        }
        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public static class QuickPanelPathHelper
{
    public static string GetRelativeDirectory(string? parentDir, string? groupFolderPath)
    {
        if (string.IsNullOrWhiteSpace(parentDir)) return string.Empty;
        if (string.IsNullOrWhiteSpace(groupFolderPath)) return parentDir;

        var parent = parentDir.TrimEnd('\\', '/');
        var baseDir = groupFolderPath.TrimEnd('\\', '/');

        if (string.Equals(parent, baseDir, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        if (parent.StartsWith(baseDir + "\\", StringComparison.OrdinalIgnoreCase) ||
            parent.StartsWith(baseDir + "/", StringComparison.OrdinalIgnoreCase))
        {
            return parent.Substring(baseDir.Length + 1);
        }

        return parent;
    }
}

