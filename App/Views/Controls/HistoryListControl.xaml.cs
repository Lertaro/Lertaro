using System.Windows;

namespace Lertaro.App.Views.Controls;

public partial class HistoryListControl : System.Windows.Controls.UserControl
{
    public HistoryListControl() => InitializeComponent();

    // Set per-instance in HistorySettingsPage.xaml: the Search and Keyword tabs share this one
    // control (just a different DataContext each), so the search box's placeholder text has to come
    // in from outside rather than being fixed here.
    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText), typeof(string), typeof(HistoryListControl), new PropertyMetadata(string.Empty));

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }
}
