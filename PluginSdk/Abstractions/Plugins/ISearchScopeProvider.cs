namespace Lertaro.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Represents a provider that publishes named search scopes for the quick search window: a scope is
/// a keyword prefix ("tf") plus a set of directories. Typing "&lt;keyword&gt; &lt;search term&gt;"
/// (e.g. "tf report") makes the host run its normal index search for the term RESTRICTED to those
/// directories instead of everywhere -- a second-stage filter over the existing index.
/// <para>
/// Unlike <see cref="ISearchableItemProvider"/>, a scope provider never enumerates or materializes
/// files: the host's own index answers scoped searches at query time, so memory and per-keystroke
/// cost stay flat no matter how large the configured folders are. A folder no host index covers is
/// skipped by the host with a logged warning rather than walked live.
/// </para>
/// </summary>
public interface ISearchScopeProvider : IPluginComponent
{
    /// <summary>
    /// Returns the scopes this provider currently publishes. Consulted on every keystroke dispatch,
    /// so implementations should return a cached list rebuilt only when their configuration changes.
    /// Scopes with a blank keyword or no folders are ignored by the host.
    /// </summary>
    IReadOnlyList<SearchScope> GetSearchScopes();
}

/// <summary>A keyword-activated directory scope published by an <see cref="ISearchScopeProvider"/>.</summary>
public sealed class SearchScope
{
    /// <summary>Case-insensitive first token that activates this scope (e.g. "tf").</summary>
    public string Keyword { get; init; } = string.Empty;

    /// <summary>Directories a search under this scope is restricted to.</summary>
    public IReadOnlyList<string> Folders { get; init; } = Array.Empty<string>();

    /// <summary>
    /// ';'-separated Win32 wildcard patterns (e.g. "*.exe;*.lnk") applied to FILE names of the
    /// scope's results -- directories always pass. "*" (the default) keeps every file.
    /// </summary>
    public string FilterPattern { get; init; } = "*";
}
