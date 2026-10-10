namespace Lertaro.PluginSdk.Services;

/// <summary>
/// The host's own search-syntax facts, for a plugin that needs to build or recognize a query token.
/// </summary>
/// <remarks>
/// The token prefix belongs to the search syntax, not to any plugin: the host's scanner lifts a word out of
/// the query by that character, then hands the token to a provider with the character still on the front.
/// So a plugin that keeps its own copy of it can only ever agree or break -- and "break" is silent: the word
/// is stripped out of the query, no provider claims it, and the search comes back empty.
///
/// The bundled CoreExtensions plugin used to keep exactly that copy (a "CustomFilterPrefix" setting), which
/// is why its settings page eventually had to explain "this must be the same character as Settings →
/// General → System". Reading it from here instead removes the copy rather than keeping two of them in
/// step -- there is nothing left to drift, and nothing to migrate.
/// </remarks>
public static class SearchSyntaxService
{
    /// <summary>Delegate set by the host application to report its configured token prefix.</summary>
    public static Func<char>? TokenPrefixFunc { get; set; }

    /// <summary>
    /// The single character that starts a plugin query token, as configured by the user under
    /// Settings → General → System. Falls back to '\' -- the shipped default -- when no host has wired
    /// this, which is the case inside a plugin's own test project.
    /// </summary>
    public static char TokenPrefix
    {
        get
        {
            try
            {
                return TokenPrefixFunc?.Invoke() ?? '\\';
            }
            catch
            {
                return '\\';
            }
        }
    }
}
