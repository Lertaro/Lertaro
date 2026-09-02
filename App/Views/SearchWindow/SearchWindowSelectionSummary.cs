using System.Windows;
using System.Windows.Controls;
using Lertaro.App.Services;
using WpfListBox = System.Windows.Controls.ListBox;

namespace Lertaro.App;

// Keeps the full window's status-bar selection summary independent from its already large code-behind.
// The list owns the selection state, while the status bar only needs a small translated projection of it.
internal sealed class SearchWindowSelectionSummary
{
    private readonly WpfListBox _list;
    private readonly TextBlock _selectedCountText;
    private readonly Window _owner;

    public SearchWindowSelectionSummary(WpfListBox list, TextBlock selectedCountText, Window owner)
    {
        _list = list;
        _selectedCountText = selectedCountText;
        _owner = owner;
        _list.SelectionChanged += OnSelectionChanged;
        TranslationManager.Instance.PropertyChanged += OnTranslationsChanged;
        _owner.Closed += OnOwnerClosed;
        Update();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => Update();

    private void OnTranslationsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == "Item[]")
            Update();
    }

    private void Update()
    {
        var count = _list.SelectedItems.Count;
        _selectedCountText.Text = string.Format(TranslationManager.Instance["Search_SelectedItemsCount"], count);
        _selectedCountText.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnOwnerClosed(object? sender, EventArgs e)
    {
        _list.SelectionChanged -= OnSelectionChanged;
        TranslationManager.Instance.PropertyChanged -= OnTranslationsChanged;
        _owner.Closed -= OnOwnerClosed;
    }
}
