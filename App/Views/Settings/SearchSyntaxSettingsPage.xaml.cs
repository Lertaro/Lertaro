using System.Windows;

namespace Lertaro.App.Views.Settings;

// Read-only help page: the only state is which chapter tab is open, held here instead of a view model
// because nothing else reads it.
public partial class SearchSyntaxSettingsPage : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty SelectedChapterProperty = DependencyProperty.Register(
        nameof(SelectedChapter), typeof(string), typeof(SearchSyntaxSettingsPage), new PropertyMetadata("Basic"));

    public string SelectedChapter
    {
        get => (string)GetValue(SelectedChapterProperty);
        set => SetValue(SelectedChapterProperty, value);
    }

    public SearchSyntaxSettingsPage() => InitializeComponent();

    private void ChapterTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string chapter })
            SelectedChapter = chapter;
    }
}
