namespace Lertaro.Core.Diagnostics;

/// <summary>
/// The one switch behind both search probes: <c>QuickSearchProbe</c> in the App and
/// <c>SearchPipelineProbe</c> in the service. Two processes, one decision -- turning diagnostics off for a
/// release must not mean remembering two constants in two assemblies.
/// </summary>
/// <remarks>
/// Turn it off for a release build by setting <see cref="CompiledIn"/> to false. Without a rebuild, setting
/// the environment variable <c>LERTARO_SEARCH_PROBE=0</c> also turns it off, which is what a machine that
/// does not want the files written can do.
///
/// The service runs as a different account from the App, so it reads its own environment: setting the
/// variable for the interactive user reaches the App only. Use a machine-level variable for both.
///
/// What the probes cost when on: one StringBuilder append per stamp, and one synchronous file append per
/// request (about 0.1 ms). They record wall-clock durations only -- never a rank, an order or a reason --
/// so they are not the ranking log the product deliberately does not write (see dev-docs/search-ranking.md).
/// </remarks>
public static class SearchProbeSwitch
{
    /// <summary>
    /// The single flag to flip for a release. Both probes read <see cref="Enabled"/>, never this one.
    /// </summary>
    public const bool CompiledIn = true;

    private static readonly bool TurnedOffByName =
        string.Equals(Environment.GetEnvironmentVariable("LERTARO_SEARCH_PROBE"), "0", StringComparison.Ordinal);

    public static bool Enabled => CompiledIn && !TurnedOffByName;
}
