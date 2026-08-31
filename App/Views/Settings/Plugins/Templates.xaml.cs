using System.Windows;
using Microsoft.Win32;

namespace Lertaro.App.Views.Settings.Plugins;

// Code-behind for Templates.xaml's FilePathFieldTemplate/FolderPathFieldTemplate Click handler --
// split out of PluginConfigWindow.xaml.cs purely to let the templates themselves live in this
// separate ResourceDictionary and keep PluginConfigWindow.xaml under the file-length limit; this
// doesn't depend on PluginConfigWindow's own state.
public partial class PluginConfigTemplates : ResourceDictionary
{
    private void BrowsePath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        var panel = btn.Parent as System.Windows.Controls.StackPanel;
        var textBox = panel?.Children.OfType<System.Windows.Controls.TextBox>().FirstOrDefault();
        if (textBox == null)
            return;

        if (btn.Tag as string == "Folder")
        {
            var dlg = new OpenFolderDialog();
            if (dlg.ShowDialog() == true)
            {
                textBox.Text = dlg.FolderName;
                textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            }
        }
        else
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            if (dlg.ShowDialog() == true)
            {
                textBox.Text = dlg.FileName;
                textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            }
        }
    }
}
