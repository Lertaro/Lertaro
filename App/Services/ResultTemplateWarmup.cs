using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using ListBox = System.Windows.Controls.ListBox;
using Window = System.Windows.Window;

namespace Lertaro.App.Services;

/// <summary>
/// Renders the result list once off-screen at startup so the first real search does not pay WPF's
/// first-render cost.
/// </summary>
/// <remarks>
/// Measured with the stage trace: applying fifty-one rows to the list costs 0.05-15 ms, but the paint that
/// follows costs 185 ms the first time and 35-100 ms afterwards -- so the data path was never the
/// bottleneck, the first render is. What that first render pays is the template's first expansion, the
/// font run and glyph cache being built, and the render pipeline warming up; none of it is per-search work,
/// and none of it can be moved off the UI thread because it IS the UI thread's work.
///
/// So it is done once, deliberately, in a window parked off every monitor: the same item template and the
/// same container style, fed rows from a real mapper call, laid out and thrown away. Nothing is shown and
/// nothing is kept, and every call is guarded -- a failed warm-up must never cost more than the stall it
/// was meant to avoid.
/// </remarks>
internal static class ResultTemplateWarmup
{
    // A first search in the quick window renders about 50 rows, so that is what this warms. It used to
    // be raised to a few hundred on the strength of a 200-row paint, but that measurement came from the
    // inline card; for the quick window it only made startup heavier without making the first search
    // any faster. Runs once, off screen.
    private const int Rows = 60;


    // Parked far enough off-screen that no monitor arrangement can bring it into view, and sized to the
    // list's own visible height so the layout pass is the one a real search does.
    private const double OffScreen = -32000;

    public static void Schedule()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
            return;

        // Background, not ApplicationIdle: Idle only runs when the dispatcher has nothing else at all, and
        // the shell keeps a startup queue (theme, tray, service handshake), so the warm-up was never
        // reached. Background still yields to input and layout, which is what the original choice protected.
        _ = dispatcher.BeginInvoke(new Action(Run), DispatcherPriority.Background);
    }

    private static void Run()
    {
        Window? window = null;
        var trace = Lertaro.Core.SearchIndex.PerfTrace.Begin();
        var outcome = "ok";
        try
        {
            var template = Application.Current?.TryFindResource("SearchResultTemplate") as DataTemplate;
            var containerStyle = Application.Current?.TryFindResource("ResultItemStyle") as Style;
            // The list style as well: it carries the ScrollViewer template and the virtualization settings,
            // so without it the warm-up built plain ListBox containers and the real list still paid its own
            // first-layout cost.
            var listStyle = Application.Current?.TryFindResource("SleekListBoxStyle") as Style;
            if (template == null)
                return;

            // Real rows from the real mapper: a synthetic object would exercise a different template path
            // (the one for "no result") and warm the wrong thing.
            var rows = ViewModels.Search.Mapping.SearchResultMapper.BuildQuickResults(null, "a", null, null, false);
            if (rows.Count == 0)
                return;

            var list = new ListBox
            {
                Style = listStyle,
                ItemTemplate = template,
                ItemContainerStyle = containerStyle,
                Width = 640,
                Height = 540,
                Background = Brushes.Black,
                BorderThickness = new Thickness(0),
                Focusable = false,
                IsHitTestVisible = false,
            };

            // Fill past the visible area: the cost being warmed is per-container, and a short list would
            // only expand the first few.
            var fill = new List<object>(Rows);
            for (var i = 0; i < Rows; i++)
                fill.Add(rows[i % rows.Count]);
            list.ItemsSource = fill;

            window = new Window
            {
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                // Deliberately NOT AllowsTransparency. A layered window renders through a different
                // pipeline (composited to a bitmap, then handed to DWM), so warming one exercises code
                // the real window never runs -- measured, the first paint stayed at 148 ms afterwards.
                // A plain window shares the real path and the work actually carries over.
                Background = Brushes.Black,
                Width = 640,
                Height = 540,
                Left = OffScreen,
                Top = OffScreen,
                Content = list,
            };

            window.Show();
            // One chunk at a time, each queued separately: UpdateLayout cannot be interrupted, so filling
            // all of it in one call makes the UI thread unresponsive for its whole duration and a keystroke
            // typed in that window does not even echo. Chunking hands the dispatcher a turn between
            // slices, so input is served while the warm-up proceeds.
            FillChunk(window, list, fill, 0);
        }
        catch
        {
            // Warming is best effort; the search still works, it just pays the first render as before.
            outcome = "failed";
        }
        finally
        {
            try
            {
                window?.Close();
            }
            catch
            {
            }

            Lertaro.Core.SearchIndex.PerfTrace.Mark("warm.template", trace, 0, outcome);
        }
    }

    // Rows per dispatcher slice. Measured: 20 rows took 40-50 ms per slice, which is exactly the "the
    // first keystroke hung" gap -- three of those back to back held the thread for ~140 ms. Eight rows
    // keeps a slice well under that, and the slices still cover more than the visible area.
    private const int ChunkRows = 8;

    private static void FillChunk(Window window, ListBox list, List<object> fill, int start)
    {
        try
        {
            if (start >= fill.Count)
                return;

            var take = Math.Min(ChunkRows, fill.Count - start);
            list.ItemsSource = fill.GetRange(0, start + take);
            window.UpdateLayout();

            if (start + take < fill.Count)
            {
                Application.Current?.Dispatcher.BeginInvoke(
                    new Action(() => FillChunk(window, list, fill, start + take)),
                    DispatcherPriority.Background);
            }
        }
        catch
        {
        }
    }
}
