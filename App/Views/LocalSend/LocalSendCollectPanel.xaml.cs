using System.Windows;
using Lertaro.App.ViewModels.LocalSend;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfButton = System.Windows.Controls.Button;

namespace Lertaro.App.Views.LocalSend;

/// <summary>Collects LocalSend files, folders, or text outside the hosting window.</summary>
public partial class LocalSendCollectPanel : WpfUserControl
{
    public event EventHandler? CloseRequested;
    public event EventHandler? NextRequested;

    public LocalSendCollectPanel() => InitializeComponent();

    private LocalSendSendViewModel? ViewModel => DataContext as LocalSendSendViewModel;
    private void FilesMode_Click(object sender, RoutedEventArgs e) => ViewModel?.SelectedMode = 0;
    private void TextMode_Click(object sender, RoutedEventArgs e) => ViewModel?.SelectedMode = 1;
    private void Cancel_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void Next_Click(object sender, RoutedEventArgs e) { ViewModel?.ProceedToStep1(); NextRequested?.Invoke(this, EventArgs.Empty); }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "选择要发送的文件" };
        if (dialog.ShowDialog() == true) ViewModel?.AddPaths(dialog.FileNames);
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog { Description = "选择要发送的文件夹", UseDescriptionForTitle = true };
        if (dialog.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedPath)) ViewModel?.AddPaths([dialog.SelectedPath]);
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { DataContext: LocalSendCollectedItem item }) ViewModel?.RemoveCollectedItem(item);
    }

    private void Panel_DragOver(object sender, WpfDragEventArgs e)
    {
        if (ViewModel?.CurrentStep == 0 && e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) { e.Effects = System.Windows.DragDropEffects.Copy; e.Handled = true; }
    }

    private void Panel_Drop(object sender, WpfDragEventArgs e)
    {
        if (ViewModel?.CurrentStep == 0 && e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths) ViewModel.AddPaths(paths);
    }
}
