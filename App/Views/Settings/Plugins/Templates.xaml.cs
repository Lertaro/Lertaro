using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.App.Views.Controls.Dialogs;

namespace Lertaro.App.Views.Settings.Plugins;

// Code-behind for Templates.xaml's FilePathFieldTemplate/FolderPathFieldTemplate Click handler --
// split out of PluginConfigWindow.xaml.cs purely to let the templates themselves live in this
// separate ResourceDictionary and keep PluginConfigWindow.xaml under the file-length limit; this
// doesn't depend on PluginConfigWindow's own state.
public partial class PluginConfigTemplates : ResourceDictionary
{
    private void IconTextBox_OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox || textBox.DataContext is not PluginConfigFieldViewModel { IsIconField: true })
            return;

        var format = e.DataObject.GetDataPresent(System.Windows.DataFormats.UnicodeText)
            ? System.Windows.DataFormats.UnicodeText
            : e.DataObject.GetDataPresent(System.Windows.DataFormats.Text) ? System.Windows.DataFormats.Text : null;
        if (format == null || e.DataObject.GetData(format) is not string pasted
            || (!SvgIconInputHelper.LooksLikeSvgDocument(pasted) && SvgIconInputHelper.IsValidPathData(pasted)))
            return;

        if (SvgIconInputHelper.TryConvert(pasted, out var pathData))
        {
            e.CancelCommand();
            var selectionStart = textBox.SelectionStart;
            var selectionLength = textBox.SelectionLength;
            textBox.Select(selectionStart, selectionLength);
            textBox.SelectedText = pathData;
            textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            return;
        }

        e.CancelCommand();
        ClearIconAndReportError(textBox);
    }

    private void IconTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox || textBox.DataContext is not PluginConfigFieldViewModel { IsIconField: true }
            || SvgIconInputHelper.IsValidPathData(textBox.Text))
            return;

        ClearIconAndReportError(textBox);
    }

    private static void ClearIconAndReportError(System.Windows.Controls.TextBox textBox)
    {
        textBox.Text = string.Empty;
        textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();

        var owner = Window.GetWindow(textBox);
        var message = TranslationManager.Instance["Plugins_IconConversionError"];
        var caption = TranslationManager.Instance["Plugins_IconConversionErrorTitle"];
        textBox.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            CustomMessageBox.Show(owner, message, caption, MessageBoxButton.OK, MessageBoxImage.Error)));
    }

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
