using System.Windows.Input;
using Lertaro.App.Helpers;

namespace Lertaro.App.ViewModels.QuickPanel;

/// <summary>One entry in the panel's tab strip, whichever kind of tab is behind it.</summary>
/// <remarks>
/// The strip is deliberately blind to that kind: a workspace of folders and a plugin's own list both
/// arrive here as a name, a way to select it and a way to close it. What closing MEANS differs, and is
/// answered by the tab source rather than here (see IQuickPanelTabSource).
/// </remarks>
public class QuickPanelTabViewModel : ViewModelBase
{
    public QuickPanelTabViewModel(string id, string label, Action onSelect, Action onClose)
    {
        Id = id;
        Label = label;
        SelectCommand = new RelayCommand(onSelect);
        CloseCommand = new RelayCommand(onClose);
    }

    public string Id { get; }

    public string Label { get; }

    public ICommand SelectCommand { get; }

    /// <summary>Takes this workspace out of the strip without deleting it.</summary>
    /// <remarks>
    /// Disables the workspace rather than removing it, the same thing the startup panel's own x does to a
    /// tab: the sources behind it took work to assemble and closing a tab is a statement about the strip,
    /// not about them. The Quick Panel settings page is where one comes back.
    /// </remarks>
    public ICommand CloseCommand { get; }

    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
