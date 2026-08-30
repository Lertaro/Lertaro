using Lertaro.Core.SearchIndex;
namespace Lertaro.Core.IndexV2.Alias;

// Alias generation for snapshot building and delta rows -- same providers, same lowercasing, same
// null-for-ASCII fast path as UpdateExtensions.GenerateAliases, without needing a RuntimeIndex
// receiver. Aliases derive from the name alone, so they bake per unique name at build time and are
// filtered per provider id at QUERY time (SearchContext.DisabledAliasIds) -- toggling a provider in
// settings never requires a rebuild.
//
// Uses GetAllProviders() (every INSTALLED provider), not GetActiveProviders() (only ENABLED ones):
// generation must not depend on the enabled/disabled toggle, or re-enabling a provider the user
// disabled earlier would surface no aliases until the next rebuild. Baking in a disabled provider's
// aliases anyway costs a little extra CPU/storage at generation time, but makes every enable/disable
// toggle a free, instant, purely query-time flip in both directions.
internal static class AliasGeneration
{
    public static string[]? Generate(string name, out byte[] providerIds)
    {
        providerIds = Array.Empty<byte>();
        if (string.IsNullOrEmpty(name) || AliasProviderRegistry.HasInvalidUtf16(name) || !AliasProviderRegistry.HasNonAscii(name))
            return null;

        List<string>? aliases = null;
        List<byte>? ids = null;
        foreach (var provider in AliasProviderRegistry.GetAllProviders())
        {
            try
            {
                if (!provider.CanHandle(name))
                    continue;
                var providerId = AliasProviderRegistry.GetProviderId(provider);
                foreach (var alias in provider.GetAliases(name))
                {
                    if (string.IsNullOrWhiteSpace(alias))
                        continue;
                    (aliases ??= new List<string>()).Add(alias.ToLowerInvariant());
                    (ids ??= new List<byte>()).Add(providerId);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[IndexV2] Alias provider failed for '{name}': {ex.Message}", LogLevel.Error);
            }
        }

        if (aliases == null || ids == null)
            return null;
        providerIds = ids.ToArray();
        return aliases.ToArray();
    }
}
