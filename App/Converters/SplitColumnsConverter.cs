namespace Lertaro.App.Converters;

public class SplitColumnsConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is string str)
        {
            return str.Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }
        return Array.Empty<string>();
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}
