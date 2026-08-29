using System.Windows;
using System.Windows.Input;

namespace Lertaro.App.Views.Settings;

public partial class PathEntryEditor : System.Windows.Controls.UserControl
{
    public PathEntryEditor() => InitializeComponent();

    public static readonly DependencyProperty EntryNameProperty = DependencyProperty.Register(
        nameof(EntryName), typeof(string), typeof(PathEntryEditor), new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty EntryPathProperty = DependencyProperty.Register(
        nameof(EntryPath), typeof(string), typeof(PathEntryEditor), new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty BrowseFolderCommandProperty = DependencyProperty.Register(
        nameof(BrowseFolderCommand), typeof(ICommand), typeof(PathEntryEditor));

    public static readonly DependencyProperty BrowseFileCommandProperty = DependencyProperty.Register(
        nameof(BrowseFileCommand), typeof(ICommand), typeof(PathEntryEditor));

    public string EntryName
    {
        get => (string)GetValue(EntryNameProperty);
        set => SetValue(EntryNameProperty, value);
    }

    public string EntryPath
    {
        get => (string)GetValue(EntryPathProperty);
        set => SetValue(EntryPathProperty, value);
    }

    public ICommand? BrowseFolderCommand
    {
        get => (ICommand?)GetValue(BrowseFolderCommandProperty);
        set => SetValue(BrowseFolderCommandProperty, value);
    }

    public ICommand? BrowseFileCommand
    {
        get => (ICommand?)GetValue(BrowseFileCommandProperty);
        set => SetValue(BrowseFileCommandProperty, value);
    }
}
