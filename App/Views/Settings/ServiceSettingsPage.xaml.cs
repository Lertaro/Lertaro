using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Lertaro.App.Helpers;
using Lertaro.App.ViewModels.Settings;
using Lertaro.Core;

namespace Lertaro.App.Views.Settings;

public partial class ServiceSettingsPage : System.Windows.Controls.UserControl
{
    public ServiceSettingsPage()
    {
        InitializeComponent();
        DataContextChanged += (s, e) =>
        {
            if (e.NewValue is SettingsViewModel vm)
            {
                vm.Log.Lines.CollectionChanged += (_, _) => OnLogLinesChanged(vm.Log.Lines);
                OnLogLinesChanged(vm.Log.Lines);
            }
        };
    }

    private void OnLogLinesChanged(IEnumerable<LogLineViewModel> lines)
    {
        // Only auto-follow to the newest lines if the user was already at (or near) the bottom --
        // otherwise a periodic refresh would yank them away while reading older lines. Checked against
        // the scroll extent from before this update, i.e. where the user actually was.
        var wasNearBottom = LogTextBox.VerticalOffset >= LogTextBox.ExtentHeight - LogTextBox.ViewportHeight - 20;
        RebuildLogDocument(lines);
        if (wasNearBottom)
            LogTextBox.ScrollToEnd();
    }

    private void RebuildLogDocument(IEnumerable<LogLineViewModel> lines)
    {
        // A page wider than any line is FlowDocument's trick for "don't wrap this text, let long lines
        // scroll horizontally instead" -- there's no direct TextWrapping=NoWrap for it. How much wider
        // has to come from the text, because that width is also the horizontal scroll range.
        var document = new FlowDocument { PagePadding = new Thickness(0) };
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        var typeface = new Typeface(LogTextBox.FontFamily, LogTextBox.FontStyle, LogTextBox.FontWeight, LogTextBox.FontStretch);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var widths = new List<double>();

        foreach (var line in lines)
        {
            var run = new Run(line.Text);
            run.SetResourceReference(TextElement.ForegroundProperty, ForegroundKeyFor(line.Level));
            paragraph.Inlines.Add(run);
            paragraph.Inlines.Add(new LineBreak());
            widths.Add(MeasureWidth(line.Text, typeface, pixelsPerDip));
        }

        document.Blocks.Add(paragraph);
        document.PageWidth = LogDocumentWidth.Compute(widths, LogTextBox.ViewportWidth);
        LogTextBox.Document = document;
    }

    // Measured rather than estimated from the character count: the log is a monospaced font, but a line
    // with CJK in it is still about twice as wide per character as an ASCII one.
    private double MeasureWidth(string text, Typeface typeface, double pixelsPerDip)
        => new FormattedText(text, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, typeface,
                             LogTextBox.FontSize, System.Windows.Media.Brushes.Black, pixelsPerDip).WidthIncludingTrailingWhitespace;

    private static string ForegroundKeyFor(LogLevel level) => level switch
    {
        LogLevel.Error => "ErrorBrush",
        LogLevel.Warn => "WarningBrush",
        LogLevel.Debug => "TextSecondary2",
        _ => "TextPrimary2"
    };

    // Shift+wheel scrolls the log horizontally instead of vertically -- there is no built-in WPF
    // gesture for this, so it's handled manually.
    private void LogTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Shift) return;
        LogTextBox.ScrollToHorizontalOffset(LogTextBox.HorizontalOffset - e.Delta);
        e.Handled = true;
    }
}
