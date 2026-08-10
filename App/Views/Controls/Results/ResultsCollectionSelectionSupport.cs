using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Lertaro.App.Views.Controls.Results;

// Split out purely to keep ResultsControl under the repository's per-file line limit. This class has
// no independent view state; it maintains collection-driven selection for the one control that owns it.
internal sealed class ResultsCollectionSelectionSupport
{
    private readonly ResultsControl _owner;
    private readonly ResultsHoverSelection _hoverSelection;
    private bool _collectionChangedPending;
    private int _anchorIndex;
    private double _anchorOffset;
    private ScrollViewer? _resultsScrollViewer;
    private bool _suppressAnchorCapture;

    public ResultsCollectionSelectionSupport(ResultsControl owner, ResultsHoverSelection hoverSelection)
    {
        _owner = owner;
        _hoverSelection = hoverSelection;

        // The template's ScrollViewer is not reachable until it is applied, but this event bubbles to
        // each list and carries that ScrollViewer as its OriginalSource.
        _owner.LstResults.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnResultsScrollChanged));
        _owner.LstGridResults.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnResultsScrollChanged));
    }

    public void UpdateItemsSource(IEnumerable? oldValue, IEnumerable? newValue)
    {
        if (oldValue is INotifyCollectionChanged oldNotify)
            oldNotify.CollectionChanged -= OnCollectionChanged;

        _owner.LstResults.ItemsSource = newValue;
        _owner.LstGridResults.ItemsSource = newValue;

        if (newValue is INotifyCollectionChanged newNotify)
            newNotify.CollectionChanged += OnCollectionChanged;
    }

    public void CaptureSelectionAnchor(int index)
    {
        // A Reset briefly deselects everything; do not let that erase the user's saved position.
        if (!_suppressAnchorCapture && index >= 0)
            _anchorIndex = index;
    }

    private void OnResultsScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer scrollViewer)
            return;
        _resultsScrollViewer = scrollViewer;

        // An extent change is layout movement, not a scroll position chosen by the user.
        if (!_suppressAnchorCapture && e.ExtentHeightChange == 0 && e.VerticalChange != 0)
            _anchorOffset = scrollViewer.VerticalOffset;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var extendsContent = sender is Helpers.ObservableRangeCollection<AppSearchResult> { LastUpdateExtendedContent: true };
        if (_collectionChangedPending) return;
        _collectionChangedPending = true;

        _owner.Dispatcher.BeginInvoke(new Action(() => ApplyCollectionChange(extendsContent)), DispatcherPriority.Render);
    }

    private void ApplyCollectionChange(bool extendsContent)
    {
        _collectionChangedPending = false;

        if (_owner.GridActions.Visibility == Visibility.Visible)
            return;

        var list = _owner.ActiveListBox;
        if (list.Items.Count == 0)
        {
            _anchorIndex = 0;
            _anchorOffset = 0;
            list.SelectedIndex = -1;
            return;
        }

        if (extendsContent)
        {
            // A tail append preserves selection. A Reset does not, so restore the user's prior anchor.
            if (list.SelectedIndex >= 0)
                return;

            _suppressAnchorCapture = true;
            try
            {
                list.SelectedIndex = Math.Clamp(_anchorIndex, 0, list.Items.Count - 1);
                if (_anchorOffset > 0 && _resultsScrollViewer != null)
                    _resultsScrollViewer.ScrollToVerticalOffset(_anchorOffset);
            }
            finally
            {
                _suppressAnchorCapture = false;
            }
            return;
        }

        // A different result set starts at its first result and at the top of the list.
        _anchorIndex = 0;
        _anchorOffset = 0;
        _suppressAnchorCapture = true;
        try
        {
            list.SelectedIndex = 0;
            list.ScrollIntoView(list.SelectedItem);
        }
        finally
        {
            _suppressAnchorCapture = false;
        }

        // Layout may synthesize MouseMove while the pointer itself remains stationary.
        _hoverSelection.Reseed();
    }
}
