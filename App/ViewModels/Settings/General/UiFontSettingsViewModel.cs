using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings.General;

/// <summary>
/// The Appearance page's UI font fallback chain: up to <see cref="UiFontSettings.MaxFamilies"/> custom
/// families tried in order, ahead of the app's built-in default. Changes apply live, like theme picks.
/// </summary>
public sealed class UiFontSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    private IReadOnlyList<UiFontSlot> _slots;

    public UiFontSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        _slots = BuildSlots();
        ChooseFontCommand = new RelayCommand<int>(position => ChooseFont(position));
        ClearFontCommand = new RelayCommand<int>(position => ClearFont(position));
    }

    public IReadOnlyList<UiFontSlot> Slots => _slots;
    public ICommand ChooseFontCommand { get; }
    public ICommand ClearFontCommand { get; }

    private void ChooseFont(int position)
    {
        var index = position - 1;
        // Rows only accept a font for their own position, or the next empty one: holes in a fallback
        // chain would leave later rows without a preceding family to fall back from.
        if (index < 0 || index > _userSettings.UiFontFamilies.Count) return;
        var current = index < _userSettings.UiFontFamilies.Count ? _userSettings.UiFontFamilies[index] : null;
        var family = SystemFontPicker.Pick(current);
        if (string.IsNullOrWhiteSpace(family)) return;
        if (index < _userSettings.UiFontFamilies.Count)
            _userSettings.UiFontFamilies[index] = family;
        else
            _userSettings.UiFontFamilies.Add(family);
        Commit();
    }

    private void ClearFont(int position)
    {
        var index = position - 1;
        if (index < 0 || index >= _userSettings.UiFontFamilies.Count) return;
        // RemoveAt rather than leaving a hole, so the remaining families keep their fallback order.
        _userSettings.UiFontFamilies.RemoveAt(index);
        Commit();
    }

    private void Commit()
    {
        _userSettings.UiFontFamilies = UiFontSettings.Normalize(_userSettings.UiFontFamilies);
        _userSettings.Save();
        if (System.Windows.Application.Current is { } app)
            AppTypography.ApplyUiFont(app.Resources, _userSettings.UiFontFamilies);
        _slots = BuildSlots();
        OnPropertyChanged(nameof(Slots));
    }

    private IReadOnlyList<UiFontSlot> BuildSlots()
    {
        var families = _userSettings.UiFontFamilies;
        return Enumerable.Range(0, UiFontSettings.MaxFamilies)
            .Select(i => new UiFontSlot(i + 1, i < families.Count ? families[i] : null, i <= families.Count))
            .ToArray();
    }
}

public sealed record UiFontSlot(int Position, string? Family, bool CanChoose)
{
    public bool HasFamily => !string.IsNullOrWhiteSpace(Family);
}
