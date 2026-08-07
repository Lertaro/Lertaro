namespace Lertaro.App.ViewModels.Search;

// Gives the scoped phase a brief opportunity to paint before the global phase competes for the same
// search and rendering resources. A completed scoped search never delays the global one.
internal static class InlineGlobalSearchGate
{
    internal const int LocalHeadStartMs = 60;

    public static async Task WaitForLocalSearchAsync(Task localSearch, CancellationToken token)
    {
        if (localSearch.IsCompleted)
            return;

        await Task.WhenAny(localSearch, Task.Delay(LocalHeadStartMs, token)).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
    }
}
