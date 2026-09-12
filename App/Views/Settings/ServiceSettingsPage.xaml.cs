using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Lertaro.App.Helpers;
using Lertaro.App.ViewModels.Settings;
using Lertaro.Core;

namespace Lertaro.App.Views.Settings;

public partial class ServiceSettingsPage : System.Windows.Controls.UserControl
{
    // The live tail's last known lines, and whether a rebuild is owed because it arrived while this page
    // was not the one on screen. See the RequestRebuild wiring in the constructor.
    private IEnumerable<LogLineViewModel>? _pendingLines;
    private bool _rebuildPending;
    private bool _rebuildQueued;

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

        // The log refreshes every 2 seconds (ServiceLogViewModel's timer), and each refresh used to rebuild
        // this whole document immediately -- measuring/formatting every line, 50-150ms of UI thread for a
        // real log. Two separate problems came from that: a recurring stall while the page was the one NOT
        // on screen, and, because this is the settings window's default page, the same cost landing on the
        // frame that opened the window -- so a click made straight afterwards (even on another tab) waited
        // behind it. Rebuild only while shown (see OnLogLinesChanged) and then only when the UI next goes
        // idle (see RequestRebuild), so opening the window and switching away both stay immediate.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && _rebuildPending)
                RequestRebuild();
        };
    }

    private void OnLogLinesChanged(IEnumerable<LogLineViewModel> lines)
    {
        _pendingLines = lines;
        RequestRebuild();
    }

    // Coalesced and deferred: a RichTextBox lays its whole FlowDocument out natively, and that is the
    // entire cost here (a CPU profile shows it all in frames with no managed method underneath), so it
    // cannot be made cheaper from here -- only kept off the frames the user is waiting on. Background
    // priority runs after the current layout/render, so the window opens with an empty log box that fills
    // in on the next idle slot. A page that has gone away again before that slot arrives leaves the flag
    // set and rebuilds when it is next shown (see the IsVisibleChanged wiring).
    private void RequestRebuild()
    {
        _rebuildPending = true;
        if (_rebuildQueued || !IsVisible) return;
        _rebuildQueued = true;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            _rebuildQueued = false;
            if (!_rebuildPending || !IsVisible) return;
            RebuildNow();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void RebuildNow()
    {
        if (_pendingLines is not { } lines) return;
        _rebuildPending = false;

        // Always follow the newest log lines: the log viewer is a live tail, so every refresh rebuilds
        // the document and jumps straight to the end, no matter where the caret was.
        RebuildLogDocument(lines);
        LogTextBox.ScrollToEnd();
    }

    private void RebuildLogDocument(IEnumerable<LogLineViewModel> lines)
    {
        // A page wider than any line is FlowDocument's trick for "don't wrap this text, let long lines
        // scroll horizontally instead" -- there's no direct TextWrapping=NoWrap for it. How much wider
        // has to come from the text, because that width is also the horizontal scroll range.
        var document = new FlowDocument { PagePadding = new Thickness(0) };
        // Mark the whole document as Simplified Chinese so WPF's per-glyph font fallback picks the CJK
        // fonts from the composite FontFamily (Microsoft YaHei UI and the other East Asian UI fonts)
        // even on a non-Chinese Windows install; Latin glyphs still come from Consolas, the first family.
        document.Language = XmlLanguage.GetLanguage("zh-CN");
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        var typeface = new Typeface(LogTextBox.FontFamily, LogTextBox.FontStyle, LogTextBox.FontWeight, LogTextBox.FontStretch);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var all = lines as IList<LogLineViewModel> ?? lines.ToList();
        foreach (var line in all)
        {
            var run = new Run(line.Text);
            run.SetResourceReference(TextElement.ForegroundProperty, ForegroundKeyFor(line.Level));
            paragraph.Inlines.Add(run);
            paragraph.Inlines.Add(new LineBreak());
        }

        // Only the widest line sets PageWidth, and the log is monospaced (Latin from Consolas, CJK from a
        // full-width East Asian font), so a line's rendered width is proportional to its WeightedLength.
        // Measuring every line with FormattedText was the bulk of this rebuild's cost; measuring only the
        // heaviest few lands on the same maximum for a fraction of the work.
        var widestCandidates = all
            .OrderByDescending(line => WeightedLength(line.Text))
            .Take(MeasuredCandidates)
            .Select(line => MeasureWidth(line.Text, typeface, pixelsPerDip));

        document.Blocks.Add(paragraph);
        document.PageWidth = LogDocumentWidth.Compute(widestCandidates, LogTextBox.ViewportWidth);
        LogTextBox.Document = document;
    }

    // How many of the heaviest lines get a real measurement. A handful rather than one, so a character
    // that renders wider than this weighting assumes cannot distort the answer.
    private const int MeasuredCandidates = 8;

    // Rough width proxy: a full-width character (CJK, kana, Hangul, fullwidth forms) occupies twice the
    // advance of a Latin one in the log's monospaced font, so counting it as two ranks lines the way
    // FormattedText would. Internal so the ranking can be tested without a visual tree.
    internal static int WeightedLength(string text)
    {
        var weight = 0;
        foreach (var ch in text)
            weight += ch >= FullWidthThreshold ? 2 : 1;
        return weight;
    }

    // Start of CJK Radicals Supplement; everything from here up (kana, CJK ideographs, Hangul, fullwidth
    // forms) renders full-width in the log's monospaced font.
    private const char FullWidthThreshold = '\u2E80';

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
