namespace Lertaro.Core.SearchIndex.Fzf;

// One AND-first OR branch. Each term set is an AND condition, while the terms inside a set remain
// alternative spellings of one typed term, including aliases supplied by providers.
internal readonly record struct FzfTermGroup(FzfTermSet[] Sets);
