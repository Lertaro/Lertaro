using System.Windows;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.App.Services.AppWindow;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Lertaro.App.Views.SpaceAnalyzer;

/// <summary>
/// Keeps preview-window integration out of the view's presentation code. It delegates all preview
/// lifetime and provider decisions to the same manager used by the full search results.
/// </summary>
internal sealed class SpaceAnalyzerPreviewSupport : IDisposable
{
    private readonly FrameworkElement _view;
    private readonly Func<string?> _selectedPath;
    private Window? _owner;

    public SpaceAnalyzerPreviewSupport(FrameworkElement view, Func<string?> selectedPath)
    {
        _view = view;
        _selectedPath = selectedPath;
        _view.Loaded += OnLoaded;
    }

    public void SetVisible(bool visible)
    {
        EnsureOwner();
        if (_owner == null)
            return;
        if (!visible)
        {
            QuickLookManager.Instance.HideFrom(_owner);
            return;
        }
        if (_owner is ISearchWindow searchWindow)
            searchWindow.LstResults.SelectedItem = null;
        SelectionChanged(_selectedPath());
    }

    public void SelectionChanged(string? path)
    {
        if (!_view.IsVisible)
            return;
        EnsureOwner();
        if (_owner == null)
            return;
        if (path == null)
            QuickLookManager.Instance.HideFrom(_owner);
        else
            QuickLookManager.Instance.UpdateOrShow(_owner, path);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => EnsureOwner();

    private void EnsureOwner()
    {
        var owner = Window.GetWindow(_view);
        if (ReferenceEquals(owner, _owner))
            return;
        _owner?.PreviewKeyDown -= OnOwnerPreviewKeyDown;
        _owner = owner;
        _owner?.PreviewKeyDown += OnOwnerPreviewKeyDown;
    }

    private void OnOwnerPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_view.IsVisible || !SearchInputHelper.IsQuickLookKey(e))
            return;
        if (_selectedPath() is not { } path || _owner == null)
            return;
        QuickLookManager.Instance.Toggle(_owner, path);
        e.Handled = true;
    }

    public void Dispose()
    {
        _view.Loaded -= OnLoaded;
        if (_owner != null)
        {
            QuickLookManager.Instance.HideFrom(_owner);
            _owner.PreviewKeyDown -= OnOwnerPreviewKeyDown;
            _owner = null;
        }
    }
}
