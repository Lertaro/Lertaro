namespace Lertaro.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Represents a provider that can claim a token out of a search query's trailing
/// "&lt;keyword&gt; :a,b,c" suffix and transform the result list for it (e.g. sort, filter). Each
/// comma-separated token is dispatched to whichever registered provider's <see cref="CanHandle"/>
/// returns true first, and results are chained through providers in token order -- so multiple
/// providers can each own a different token and compose within one query.
/// </summary>
public interface IQueryTokenProvider : IPluginComponent
{

    /// <summary>
    /// Returns true if this provider understands the given token (e.g. "s", ".txt.doc", or any
    /// custom token this plugin defines). Called once per token in the query's suffix; a token no
    /// provider claims is treated as an unsupported/typo'd filter -- the file/directory results are
    /// dropped rather than silently applying only the tokens that were recognized (everything else in
    /// the query window -- applications, instant results like a calculator answer, ... -- is unrelated
    /// to a file filter and is unaffected either way).
    /// </summary>
    bool CanHandle(string token);

    /// <summary>
    /// Transforms <paramref name="results"/> (already narrowed to file/directory results only -- no
    /// applications, section headers, or other synthetic/non-file rows) for the recognized
    /// <paramref name="token"/>. Return the transformed list (reordered and/or filtered, from the same
    /// result instances, never with items added). This is purely a list transform -- the host owns
    /// composing whatever else belongs in the final query window around it, deciding what a shrunk or
    /// empty result means for the rest of the UI, and rendering any "N more"/"no results" indicator.
    /// </summary>
    Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results);

    /// <summary>
    /// The literal text this token fuzzy-matches against a result's name/path, if any -- the host
    /// folds this into what gets highlighted in the UI alongside the query's main keyword, so a result
    /// kept by this token (e.g. because a path segment matched it) visibly shows why. Return null for
    /// a token whose effect isn't a text match at all (a sort key, an extension filter, ...), which is
    /// simply not included in highlighting. Defaults to null so existing providers are unaffected.
    /// </summary>
    string? GetHighlightText(string token) => null;
}
