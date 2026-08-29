using System.Windows.Input;

namespace Lertaro.App.ViewModels.Settings;

// Split out to keep the settings view model focused on list coordination and under the repository's file limit.
public sealed class ExclusionRuleItem : ViewModelBase
{
    private string _value;
    private string _editValue;
    private bool _isEditing;

    public ExclusionRuleItem(string value)
    {
        _value = value;
        _editValue = value;
    }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
                return;
            _value = value;
            OnPropertyChanged();
        }
    }

    public string EditValue
    {
        get => _editValue;
        set
        {
            if (_editValue == value)
                return;
            _editValue = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing == value)
                return;
            _isEditing = value;
            OnPropertyChanged();
        }
    }
}
