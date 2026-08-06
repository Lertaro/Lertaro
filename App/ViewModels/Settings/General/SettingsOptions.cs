namespace Lertaro.App.ViewModels.Settings.General;

// A mutable, stable-identity option for combos bound via SelectedValue/SelectedValuePath: bind
// ItemsSource to this list ONCE and never swap it, updating only Label in place (via PropertyChanged)
// on a language change. Reassigning ItemsSource wholesale instead transiently nulls the bound
// SelectedValue while WPF regenerates items, which is what breaks the combo across a language switch.
public sealed class LabeledOption : ViewModelBase
{
    private string _label;

    public LabeledOption(string value, string label)
    {
        Value = value;
        _label = label;
    }

    public string Value { get; }

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public override string ToString() => Label;
}

public sealed record LogLevelOption(string Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record ThemeOption(string Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record LanguageOption(string Value, string Label)
{
    public override string ToString() => Label;

    public static string GetLanguageDisplayName(string cultureCode)
    {
        try
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo(cultureCode);
            var nativeName = culture.NativeName;
            if (!string.IsNullOrEmpty(nativeName))
            {
                return char.ToUpper(nativeName[0]) + nativeName.Substring(1);
            }
        }
        catch { }

        return cultureCode;
    }
}
