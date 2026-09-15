namespace Lertaro.Core.SearchIndex.Fzf;

internal readonly record struct FzfTermSet(FzfTerm[] Terms);

// AliasForm marks a provider-supplied spelling so highlighting can distinguish it from user input.
internal readonly record struct FzfTerm(FzfTermKind Kind, bool Inverse, string Text, bool CaseSensitive, bool AliasForm = false);

internal readonly record struct FzfPatternResult(int Score, int MinBegin, int MinEnd, int MaxEnd, bool ValidOffsetFound);
