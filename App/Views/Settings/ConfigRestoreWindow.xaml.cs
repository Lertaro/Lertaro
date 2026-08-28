using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services.Theme;

namespace Lertaro.App.Views.Settings;

/// <summary>
/// Small picker dialog behind the About page's "Restore Config": lists the .bak.N backups of
/// user-settings.json newest first (order already computed by UserConfigBackups.Enumerate) and
/// hands the chosen file's path back via <see cref="SelectedBackupPath"/> once OK or a double-click
/// confirms. The borderless shell is copied from PluginFieldPromptWindow.
/// </summary>
public partial class ConfigRestoreWindow : Window
{
    // One row per backup. Display is preformatted once here rather than per binding: the timestamp
    // needs invariant formatting (a custom "yyyy-MM-dd HH:mm" pattern, whose ":" would otherwise be
    // swapped for the current culture's time separator), and the row template stays a plain TextBlock.
    public sealed record BackupItem(string Path, DateTime ModifiedTime, string Display);

    internal ConfigRestoreWindow(IReadOnlyList<(string Path, DateTime ModifiedTime)> backups)
    {
        InitializeComponent();
        SystemMenuBlocker.Attach(this);
        AltTabExcluder.Attach(this);
        ThemedWindowIconHelper.Apply(this);
        ThemedWindowIconHelper.Apply(TitleBarLogo, this);

        var items = new List<BackupItem>(backups.Count);
        foreach (var (path, modified) in backups)
        {
            var stamp = modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            items.Add(new BackupItem(path, modified, $"{stamp}   {Path.GetFileName(path)}"));
        }
        BackupsList.ItemsSource = items;
        BtnOK.IsEnabled = false;
    }

    /// <summary>Path of the backup to restore once the dialog is confirmed; null when cancelled.</summary>
    public string? SelectedBackupPath { get; private set; }

    private void BackupsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        BtnOK.IsEnabled = BackupsList.SelectedItem != null;

    private void BtnOK_Click(object sender, RoutedEventArgs e) => Confirm();

    // A double-click reads as OK, same as picking an entry in any list-and-confirm dialog.
    private void BackupsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Confirm();

    private void Confirm()
    {
        if (BackupsList.SelectedItem is not BackupItem item) return;
        SelectedBackupPath = item.Path;
        DialogResult = true;
        Close();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    // SizeToContent + CenterOwner positions against a stale pre-measure size (the same quirk
    // PluginFieldPromptWindow's Window_Loaded fixes), so recenter once the real size is known.
    private void Window_Loaded(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Owner == null) return;
            Left = Owner.Left + (Owner.ActualWidth - ActualWidth) / 2;
            Top = Owner.Top + (Owner.ActualHeight - ActualHeight) / 2;
        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
}
