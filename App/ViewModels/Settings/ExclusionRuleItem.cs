using System.Collections;
using System.Windows.Input;

using Lertaro.App.Services;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings;

// Split out to keep the settings view model focused on list coordination and under the repository's file limit.
public sealed class ExclusionRuleItem : ViewModelBase
{
    private string _value;
    private string _editValue;
    private bool _isEditing;
    private IndexRuleEffect _effect;
    private IndexRuleEffect _editEffect;
    private bool _isConflict;
    private string _conflictTooltip = string.Empty;

    public ExclusionRuleItem(string value, IndexRuleEffect effect = IndexRuleEffect.Normal)
    {
        _value = value;
        _editValue = value;
        _effect = effect;
        _editEffect = effect;
        Kind = ParseKind(value);
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
            OnPropertyChanged(nameof(KindText));
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

    public IndexRuleKind Kind { get; private set; }

    public IndexRuleEffect Effect
    {
        get => _effect;
        set
        {
            if (_effect == value)
                return;
            _effect = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EffectValue));
            OnPropertyChanged(nameof(EffectText));
        }
    }

    public IndexRuleEffect EditEffect
    {
        get => _editEffect;
        set
        {
            if (_editEffect == value)
                return;
            _editEffect = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EditEffectValue));
        }
    }

    /// <summary>Combo-friendly spelling of <see cref="Effect"/>.</summary>
    public string EffectValue
    {
        get => Effect.ToString();
        set => Effect = ParseEffect(value);
    }

    public string EditEffectValue
    {
        get => EditEffect.ToString();
        set => EditEffect = ParseEffect(value);
    }

    public string EffectText => TranslationManager.Instance[Effect switch
    {
        IndexRuleEffect.Exclude => "Exclusions_EffectExclude",
        IndexRuleEffect.Low => "Exclusions_EffectLow",
        IndexRuleEffect.High => "Exclusions_EffectHigh",
        _ => "Exclusions_EffectNormal",
    }];

    public string KindText => TranslationManager.Instance[Kind switch
    {
        IndexRuleKind.Path => "Exclusions_KindPath",
        IndexRuleKind.Regex => "Exclusions_KindRegex",
        _ => "Exclusions_KindGlob",
    }];

    public bool IsConflict
    {
        get => _isConflict;
        set
        {
            if (_isConflict == value)
                return;
            _isConflict = value;
            OnPropertyChanged();
        }
    }

    public string ConflictTooltip
    {
        get => _conflictTooltip;
        set
        {
            if (_conflictTooltip == value)
                return;
            _conflictTooltip = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Sort group: complete paths, then globs, then regexes.</summary>
    public int KindRank => Kind switch
    {
        IndexRuleKind.Path => 0,
        IndexRuleKind.Glob => 1,
        _ => 2,
    };

    public void RefreshKind() => Kind = ParseKind(Value);

    private static IndexRuleKind ParseKind(string value) => IndexRuleSyntax.TryParse(value, IndexRuleEffect.Normal, out var parsed, out _)
        ? parsed.Kind
        : IndexRuleKind.Glob;

    private static IndexRuleEffect ParseEffect(string? value) => Enum.TryParse<IndexRuleEffect>(value, ignoreCase: true, out var effect)
        ? effect
        : IndexRuleEffect.Normal;
}

/// <summary>Complete paths first, then globs, then regexes; alphabetical inside each group.</summary>
public sealed class IndexRuleSortComparer : IComparer
{
    public int Compare(object? x, object? y)
    {
        if (x is not ExclusionRuleItem left || y is not ExclusionRuleItem right)
            return 0;

        var byKind = left.KindRank.CompareTo(right.KindRank);
        return byKind != 0
            ? byKind
            : StringComparer.OrdinalIgnoreCase.Compare(left.Value, right.Value);
    }
}
