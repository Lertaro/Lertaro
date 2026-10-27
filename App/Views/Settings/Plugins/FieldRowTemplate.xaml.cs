using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.App.Views.Controls;
using Lertaro.App.Views.Controls.Dialogs;

namespace Lertaro.App.Views.Settings.Plugins;

// Code-behind for FieldRowTemplate.xaml's array master/detail event handlers -- split out of
// PluginConfigSection.xaml.cs purely to let the recursive field-row template live in this separate
// ResourceDictionary and keep PluginConfigSection.xaml under the file-length limit; none of these
// depend on PluginConfigSection's own state.
public partial class PluginConfigFieldRowTemplate : ResourceDictionary
{
    private void ImportEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PluginConfigFieldViewModel { SupportsEntryTransfer: true } field } element) return;
        var owner = Window.GetWindow(element);
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON (*.json)|*.json",
                CheckFileExists = true,
                Title = TranslationManager.Instance["Plugins_EntryImport"]
            };
            if (dialog.ShowDialog(owner) != true) return;
            var warning = field.ImportEntry(PluginConfigEntryTransfer.ReadFile(dialog.FileName));
            if (warning.Length != 0)
                CustomMessageBox.Show(owner, warning, TranslationManager.Instance["Plugins_EntryImport"], MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or System.IO.IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            ShowEntryError(owner, ex);
        }
    }

    private void ExportEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PluginConfigFieldViewModel { CanExportEntry: true } field } element) return;
        var owner = Window.GetWindow(element);
        try
        {
            var bytes = field.ExportEntry();
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON (*.json)|*.json",
                DefaultExt = ".json",
                AddExtension = true,
                FileName = PluginConfigEntryTransfer.SuggestedFileName(field.SelectedArrayItem!),
                Title = TranslationManager.Instance["Plugins_EntryExport"]
            };
            if (dialog.ShowDialog(owner) == true) PluginConfigEntryTransfer.WriteFile(dialog.FileName, bytes);
        }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or System.IO.IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            ShowEntryError(owner, ex);
        }
    }

    private static void ShowEntryError(Window? owner, Exception error) => CustomMessageBox.Show(owner,
        PluginConfigEntryTransfer.ErrorMessage(error),
        TranslationManager.Instance["Plugins_EntryErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Error);

    private void InlineEditorHost_EditCompleted(object sender, RoutedEventArgs e)
    {
        if (sender is not InlineEditorHost { DataContext: PluginConfigFieldViewModel { IsIconField: true } field } host
            || SvgIconInputHelper.IsValidPathData(field.Value?.ToString()))
            return;

        field.Value = string.Empty;
        var owner = Window.GetWindow(host);
        var message = TranslationManager.Instance["Plugins_IconConversionError"];
        var caption = TranslationManager.Instance["Plugins_IconConversionErrorTitle"];
        host.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            CustomMessageBox.Show(owner, message, caption, MessageBoxButton.OK, MessageBoxImage.Error)));
    }

    // Brings the newly-added (and newly-selected) row into view when the master list has more
    // items than fit -- otherwise Add silently appends off-screen below the visible scroll area.
    private void ArrayMasterList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox { SelectedItem: not null } listBox)
            listBox.ScrollIntoView(listBox.SelectedItem);
    }

    /// <summary>
    /// Shift+wheel scrolls the object array table sideways; a plain wheel is left to whichever column
    /// of the table can scroll under the pointer, and handed to the page when neither can -- including
    /// the one under the pointer once it has reached its end.
    /// </summary>
    /// <remarks>
    /// A ScrollViewer does not scroll sideways on the wheel of its own accord, so without the Shift
    /// branch the horizontal bar this table can grow is reachable only by dragging it.
    ///
    /// The vertical case is the opposite problem. A ScrollViewer swallows the wheel even with its
    /// vertical scrolling disabled, so a plain scroll over a column that cannot scroll would stall the
    /// page rather than move it, and nothing bubbles out on its own to fix that: that one has to be
    /// re-raised at the parent by hand. A column that CAN scroll has to be left alone, because its own
    /// ScrollViewer is the one that knows the system's wheel metrics -- answering for it here with
    /// LineUp/LineDown moves a single line, 16px a notch against the 48px the wheel moves anywhere else
    /// in the app (measured with scratch/arrayscroll).
    /// </remarks>
    private void ArrayScroll_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is not System.Windows.Controls.ScrollViewer scrollViewer) return;

        if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Shift)
        {
            if (e.Delta < 0)
                scrollViewer.LineRight();
            else
                scrollViewer.LineLeft();

            e.Handled = true;
            return;
        }

        if (e.OriginalSource is DependencyObject depObj)
        {
            // The master list and the detail panel each scroll on their own bar; whichever one is under
            // the pointer keeps the event, but only while it can still move that way. A column already
            // at its end is not the one to answer for the wheel: it falls through to the page instead.
            // Otherwise a detail panel capped to the pane keeps the wheel for good -- its
            // ScrollableHeight stays above zero once it hits bottom, so the page under it stops moving.
            // The walk starts at the hit element itself: a wheel over a column's blank area -- below its
            // last row, or beside the content -- hits the ScrollViewer rather than one of its children,
            // and starting one level up would skip the very column under the pointer and hand a notch
            // that column still had room for to a page that cannot move.
            // The walk stops at the table's own scroller, past which the wheel belongs to the page.
            for (var column = depObj as System.Windows.Controls.ScrollViewer ?? FindParent<System.Windows.Controls.ScrollViewer>(depObj);
                 column != null && column != scrollViewer;
                 column = FindParent<System.Windows.Controls.ScrollViewer>(column))
            {
                if (CanScroll(column, e.Delta)) return;
            }
        }

        e.Handled = true;
        var bubbled = new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender,
        };
        (scrollViewer.Parent as UIElement)?.RaiseEvent(bubbled);
    }

    // Whether that column's own bar still has room in the wheel's direction. Every scroller here is
    // measured in pixels (CanContentScroll="False"), which is also what makes an item-scrolled one read
    // as immovable: it reports an extent of 1 against a viewport of 1.
    private static bool CanScroll(System.Windows.Controls.ScrollViewer column, int delta) =>
        delta < 0
            ? column.VerticalOffset < column.ScrollableHeight
            : column.VerticalOffset > 0;

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null && parent is not T)
        {
            parent = VisualTreeHelper.GetParent(parent);
        }
        return parent as T;
    }

    // The settings pane's scroller: the nearest ancestor ScrollViewer that scrolls vertically. The
    // table's own scroller is horizontal-only, so it is skipped.
    private static System.Windows.Controls.ScrollViewer? FindPaneScrollViewer(DependencyObject child)
    {
        for (var parent = VisualTreeHelper.GetParent(child); parent != null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is System.Windows.Controls.ScrollViewer { VerticalScrollBarVisibility: not System.Windows.Controls.ScrollBarVisibility.Disabled } pane)
                return pane;
        }
        return null;
    }

    // The floor for the detail column: a pane short enough to leave less than this scrolls the page
    // for the row instead of crushing the fields into a sliver.
    private const double MinDetailPanelHeight = 160;

    // The array row's own bottom chrome, which the cap has to leave room for or the page keeps
    // scrolling those pixels anyway: the field's margin (4), the row's bottom padding (10) and the
    // gap below the row (6).
    private const double RowBottomChrome = 20;

    // The detail column used to be exactly as tall as its content, so a field with a dozen sub-fields
    // (custom commands, custom actions) made the row taller than the pane and the page's single
    // scrollbar moved both columns together -- unlike a three-field array (core's custom filters),
    // where the row fits the pane and each column already scrolled on its own. Capping the detail
    // column at the height still left in the pane splits them: it scrolls itself, the master list
    // mirrors the capped height and scrolls itself, and the row stops stretching the page.
    private static void CapDetailPanelToPane(FrameworkElement detailPanel)
    {
        // The prompt dialog sizes itself to its content, so its scroller's viewport IS that content:
        // capping against it would take the difference off the panel on every pass. There the dialog
        // just grows, which is the whole point of it.
        if (Window.GetWindow(detailPanel) is PluginFieldPromptWindow) return;

        var pane = FindPaneScrollViewer(detailPanel);
        if (pane?.Content is not Visual paneContent) return;

        var above = detailPanel.TransformToVisual(paneContent).Transform(new System.Windows.Point()).Y;
        // Rows above that already fill the pane would otherwise leave a sliver of table: there the
        // table takes the whole pane and the page scrolls the rest of the way down to it.
        if (above >= pane.ViewportHeight) above = 0;

        detailPanel.MaxHeight = Math.Max(MinDetailPanelHeight, pane.ViewportHeight - above - RowBottomChrome);
    }

    // Keeps an array field's master list column exactly as tall as its detail panel, so only the
    // ListBox itself scrolls (not the whole page). Driven from code rather than a pure Grid Auto-row
    // + ElementName height binding, because that combination feeds back on itself: an unresolved
    // first-pass height lets the master list's own (unbounded) natural size leak into the row's Auto
    // height, which the detail panel -- if it were Stretch-aligned -- would then adopt too, permanently
    // locking in an inflated value. Doing the sync explicitly after each real layout avoids that.
    private void ArrayDetailPanel_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement detailPanel) return;

        CapDetailPanelToPane(detailPanel);
        SyncArrayMasterListHeight(detailPanel);
        TriggerWindowResizeToFit(detailPanel);

        // Resizing the window changes the pane's height without touching this panel's own size, so
        // nothing else here would re-run the cap. Unloaded pairs with Loaded.
        if (Window.GetWindow(detailPanel) is { } window)
        {
            SizeChangedEventHandler recap = (_, _) => CapDetailPanelToPane(detailPanel);
            window.SizeChanged += recap;
            detailPanel.Unloaded += (_, _) => window.SizeChanged -= recap;
        }
    }

    private void ArrayDetailPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement detailPanel) return;

        // Re-running the cap here is what picks up a row above the table changing height, e.g. a
        // description rewrapping. Same pane and same offset, so it settles on the same value.
        CapDetailPanelToPane(detailPanel);
        SyncArrayMasterListHeight(detailPanel);
        TriggerWindowResizeToFit(detailPanel);
    }

    private static void TriggerWindowResizeToFit(object sender)
    {
        if (sender is FrameworkElement element && Window.GetWindow(element) is PluginFieldPromptWindow promptWindow)
        {
            promptWindow.ResizeToFit();
        }
    }

    private static void SyncArrayMasterListHeight(FrameworkElement detailPanel)
    {
        if (FindParent<System.Windows.Controls.Grid>(detailPanel) is not System.Windows.Controls.Grid parentGrid) return;

        var masterGrid = parentGrid.Children.OfType<System.Windows.Controls.Grid>()
            .FirstOrDefault(g => System.Windows.Controls.Grid.GetColumn(g) == 0);
        if (masterGrid == null) return;

        // An empty array has no selected item, so the detail panel collapses to zero height. Don't
        // mirror that onto the master list too -- that would hide the Add button along with it, which
        // is exactly what's needed to add the very first item. Fall back to its own natural size instead.
        masterGrid.Height = detailPanel.ActualHeight > 0 ? detailPanel.ActualHeight : double.NaN;
    }
}
