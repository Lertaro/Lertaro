using System.Windows;
using System.Windows.Controls;
using Lertaro.App.Views.Controls;

namespace Lertaro.App.Tests.Views.Controls;

// Asserts the actual observable outcome (the real TextBox's Text/caret after the handler runs), not an
// intermediate DataObject/FormatToApply value WPF's internal paste command may or may not honor -- see
// SearchBoxPasteHandler's implementation for why the DataObject-replacement approach was abandoned after it
// looked correct here but silently pasted nothing in real use.
[TestClass]
public sealed class SearchBoxControlPasteTests
{
    private static DataObjectPastingEventArgs MakeArgs(string text) =>
        new(new DataObject(DataFormats.UnicodeText, text), false, DataFormats.UnicodeText);

    [StaTestMethod]
    public void OnSearchTextPasting_MultiLineText_ReplacesSelectionWithJoinedPipeQueryAndCancelsDefaultPaste()
    {
        var textBox = new TextBox { Text = "" };
        var e = MakeArgs("123\n456\n678");

        SearchBoxPasteHandler.Handle(textBox, e);

        Assert.AreEqual("123 | 456 | 678", textBox.Text);
        Assert.AreEqual(textBox.Text.Length, textBox.SelectionStart);
        Assert.AreEqual(0, textBox.SelectionLength);
        Assert.IsTrue(e.CommandCancelled);
    }

    [StaTestMethod]
    public void OnSearchTextPasting_MultiLineText_ReplacesOnlyTheCurrentSelection()
    {
        var textBox = new TextBox { Text = "before AFTER" };
        textBox.Select(7, 5); // selects "AFTER"
        var e = MakeArgs("123\n456");

        SearchBoxPasteHandler.Handle(textBox, e);

        Assert.AreEqual("before 123 | 456", textBox.Text);
    }

    [StaTestMethod]
    public void OnSearchTextPasting_CrlfLineEndings_HandledSameAsLf()
    {
        var textBox = new TextBox { Text = "" };
        var e = MakeArgs("123\r\n456\r\n678");

        SearchBoxPasteHandler.Handle(textBox, e);

        Assert.AreEqual("123 | 456 | 678", textBox.Text);
        Assert.IsTrue(e.CommandCancelled);
    }

    [StaTestMethod]
    public void OnSearchTextPasting_BlankAndWhitespaceOnlyLines_SkipsThem()
    {
        var textBox = new TextBox { Text = "" };
        var e = MakeArgs("123\n\n   \n456\n");

        SearchBoxPasteHandler.Handle(textBox, e);

        Assert.AreEqual("123 | 456", textBox.Text);
    }

    [StaTestMethod]
    public void OnSearchTextPasting_SingleLineText_LeavesTextBoxUntouchedAndDoesNotCancel()
    {
        var textBox = new TextBox { Text = "" };
        var e = MakeArgs("report.docx");

        SearchBoxPasteHandler.Handle(textBox, e);

        Assert.AreEqual("", textBox.Text);
        Assert.IsFalse(e.CommandCancelled);
    }

    [StaTestMethod]
    public void OnSearchTextPasting_OnlyBlankLines_LeavesTextBoxUntouchedAndDoesNotCancel()
    {
        var textBox = new TextBox { Text = "" };
        var e = MakeArgs("\n\n   \n");

        SearchBoxPasteHandler.Handle(textBox, e);

        Assert.AreEqual("", textBox.Text);
        Assert.IsFalse(e.CommandCancelled);
    }

    [StaTestMethod]
    public void OnSearchTextPasting_NoTextDataPresent_DoesNotThrowAndDoesNotCancel()
    {
        // DataObjectPastingEventArgs' own constructor requires formatToApply to actually be present on
        // the DataObject, so a non-text format (e.g. a pasted bitmap) is what simulates "no text data" here.
        var textBox = new TextBox { Text = "" };
        var dataObject = new DataObject(DataFormats.Bitmap, new object());
        var e = new DataObjectPastingEventArgs(dataObject, false, DataFormats.Bitmap);

        SearchBoxPasteHandler.Handle(textBox, e);

        Assert.AreEqual("", textBox.Text);
        Assert.IsFalse(e.CommandCancelled);
    }

    [StaTestMethod]
    public void OnSearchTextPasting_SenderIsNotATextBox_DoesNotThrow()
    {
        var e = MakeArgs("123\n456");

        SearchBoxPasteHandler.Handle(new object(), e);

        Assert.IsFalse(e.CommandCancelled);
    }
}
