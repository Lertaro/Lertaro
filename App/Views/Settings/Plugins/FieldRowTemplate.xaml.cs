using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.App.Views.Controls;
using Lertaro.App.Views.Controls.Dialogs;

namespace Lertaro.App.Views.Settings.Plugins;

// Code-behind for FieldRowTemplate.xaml's array master/detail event handlers -- split out of
// PluginConfigWindow.xaml.cs purely to let the recursive field-row template live in this separate
// ResourceDictionary and keep PluginConfigWindow.xaml under the file-length limit; none of these
// depend on PluginConfigWindow's own state.
public partial class PluginConfigFieldRowTemplate : ResourceDictionary
{
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
    /// Shift+wheel scrolls the object array table sideways; a plain wheel goes back to the page.
    /// </summary>
    /// <remarks>
    /// A ScrollViewer does not scroll sideways on the wheel of its own accord, so without the Shift
    /// branch the horizontal bar this table can grow is reachable only by dragging it. The rest is a
    /// separate problem: a ScrollViewer swallows the wheel even with its vertical scrolling disabled,
    /// so a plain scroll anywhere over the table would stall the page rather than move it, and nothing
    /// bubbles out on its own to fix that. The event has to be re-raised at the parent by hand.
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
            var listBox = FindParent<System.Windows.Controls.ListBox>(depObj);
            if (listBox != null)
            {
                var listScroller = FindChild<System.Windows.Controls.ScrollViewer>(listBox);
                if (listScroller != null && listScroller.ScrollableHeight > 0)
                {
                    if (e.Delta < 0)
                        listScroller.LineDown();
                    else
                        listScroller.LineUp();

                    e.Handled = true;
                    return;
                }
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

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null && parent is not T)
        {
            parent = VisualTreeHelper.GetParent(parent);
        }
        return parent as T;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild) return typedChild;
            var subChild = FindChild<T>(child);
            if (subChild != null) return subChild;
        }
        return null;
    }

    // Keeps an array field's master list column exactly as tall as its detail panel, so only the
    // ListBox itself scrolls (not the whole page). Driven from code rather than a pure Grid Auto-row
    // + ElementName height binding, because that combination feeds back on itself: an unresolved
    // first-pass height lets the master list's own (unbounded) natural size leak into the row's Auto
    // height, which the detail panel -- if it were Stretch-aligned -- would then adopt too, permanently
    // locking in an inflated value. Doing the sync explicitly after each real layout avoids that.
    private void ArrayDetailPanel_Loaded(object sender, RoutedEventArgs e)
    {
        SyncArrayMasterListHeight(sender);
        TriggerWindowResizeToFit(sender);
    }

    private void ArrayDetailPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SyncArrayMasterListHeight(sender);
        TriggerWindowResizeToFit(sender);
    }

    private static void TriggerWindowResizeToFit(object sender)
    {
        if (sender is FrameworkElement element && Window.GetWindow(element) is PluginFieldPromptWindow promptWindow)
        {
            promptWindow.ResizeToFit();
        }
    }

    private static void SyncArrayMasterListHeight(object sender)
    {
        if (sender is not FrameworkElement detailPanel) return;
        if (VisualTreeHelper.GetParent(detailPanel) is not System.Windows.Controls.Grid parentGrid) return;

        var masterGrid = parentGrid.Children.OfType<System.Windows.Controls.Grid>()
            .FirstOrDefault(g => System.Windows.Controls.Grid.GetColumn(g) == 0);
        if (masterGrid == null) return;

        // An empty array has no selected item, so the detail panel collapses to zero height. Don't
        // mirror that onto the master list too -- that would hide the Add button along with it, which
        // is exactly what's needed to add the very first item. Fall back to its own natural size instead.
        masterGrid.Height = detailPanel.ActualHeight > 0 ? detailPanel.ActualHeight : double.NaN;
    }
}
