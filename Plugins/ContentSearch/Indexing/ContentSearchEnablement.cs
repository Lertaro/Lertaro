namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// Resolves whether the content-search runtime is needed. Both query providers share one index,
/// so the runtime must stay available while either provider is enabled.
/// </summary>
internal static class ContentSearchEnablement
{
    internal static bool IsRuntimeEnabled(
        Func<string, string, string, bool> isComponentEnabled,
        string dllName)
        => isComponentEnabled(dllName, "InstantProvider", "ContentSearchInstantProvider")
            || isComponentEnabled(dllName, "FullSearchFileResultProvider", "ContentSearchInstantProvider");
}
