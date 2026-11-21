using System.Windows;
using Lertaro.App.Helpers;
using DataFormats = System.Windows.DataFormats;
using TextBox = System.Windows.Controls.TextBox;

namespace Lertaro.App.Views.Controls;

// Handles SearchBoxControl paste events; SearchTextPasteFormatter supplies the text rules.
internal static class SearchBoxPasteHandler
{
    internal static void Handle(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;
        if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText) && !e.DataObject.GetDataPresent(DataFormats.Text))
            return;

        var text = e.DataObject.GetData(DataFormats.UnicodeText) as string ?? e.DataObject.GetData(DataFormats.Text) as string;
        if (!SearchTextPasteFormatter.TryFormatMultiLine(text, out var joined))
            return;

        var insertAt = textBox.SelectionStart;
        textBox.SelectedText = joined;
        textBox.SelectionStart = insertAt + joined.Length;
        textBox.SelectionLength = 0;
        e.CancelCommand();
    }
}
