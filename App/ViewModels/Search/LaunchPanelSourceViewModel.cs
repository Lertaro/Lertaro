using System.Collections.ObjectModel;

namespace Lertaro.App.ViewModels.Search;

public sealed class LaunchPanelSourceViewModel : ViewModelBase
{
    private bool _isSelected;

    public LaunchPanelSourceViewModel(string id, string name, IEnumerable<AppSearchResult> items)
    {
        Id = id;
        Name = name;
        Items = new ObservableCollection<AppSearchResult>(items);
    }

    public string Id { get; }
    public string Name { get; }
    public ObservableCollection<AppSearchResult> Items { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
