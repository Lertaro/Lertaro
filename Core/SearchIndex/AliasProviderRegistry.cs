using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.SearchIndex;

public static class AliasProviderRegistry
{
    private static readonly ConcurrentBag<IAliasProvider> Providers = new();
    private static readonly ConcurrentDictionary<string, byte> ProviderIdMap = new(StringComparer.OrdinalIgnoreCase);

    // The same ids again, keyed by the instance. GetComponentId builds a string from the assembly path
    // on every call, which is fine for the registration-time and settings-time callers but not for the
    // highlight mask, which asks once per candidate -- thousands of times per keystroke. Registration
    // already knows the answer, so it records it here.
    private static readonly ConcurrentDictionary<IAliasProvider, byte> IdByInstance = new(ReferenceEqualityComparer.Instance);
    private static byte _nextId = 0;

    public static Func<IAliasProvider, bool> FilterFunc { get; set; } = _ => true;

    public static void Register(IAliasProvider provider)
    {
        if (provider == null) return;
        Providers.Add(provider);

        var componentId = GetComponentId(provider);
        var id = ProviderIdMap.GetOrAdd(componentId, _ => _nextId++);
        IdByInstance[provider] = id;
        Logger.Log($"[AliasProviderRegistry] Registered alias provider: {provider.Name} with ID: {id} ({componentId})");
    }

    public static byte GetProviderId(IAliasProvider provider)
        => IdByInstance.TryGetValue(provider, out var cached) ? cached
            : ProviderIdMap.TryGetValue(GetComponentId(provider), out var id) ? id : (byte)0;

    public static byte GetProviderIdByComponentId(string componentId) => ProviderIdMap.TryGetValue(componentId, out var id) ? id : (byte)255; // 255 represents not found

    private static string GetComponentId(IAliasProvider provider)
    {
        var dllName = Path.GetFileName(provider.GetType().Assembly.Location);
        var typeName = provider.GetType().Name;
        return $"{dllName}::AliasProvider::{typeName}";
    }

    public static IEnumerable<IAliasProvider> GetActiveProviders()
    {
        foreach (var prov in Providers)
        {
            if (FilterFunc(prov))
            {
                yield return prov;
            }
        }
    }

    /// <summary>
    /// Returns ALL registered alias providers, regardless of the enabled/disabled filter.
    /// Used by the settings UI to show unchecked (disabled) providers instead of hiding them, and by
    /// AliasGeneration -- a provider that's merely disabled must still have its aliases baked in, so
    /// re-enabling it later is a free query-time flip instead of needing a rebuild (see
    /// ComputeProvidersFingerprint).
    /// </summary>
    public static IEnumerable<IAliasProvider> GetAllProviders() => Providers;

    // Identifies "the exact set of installed alias providers, at their exact versions" that
    // AliasGeneration baked into a snapshot's alias data. Deliberately independent of FilterFunc
    // (enabled/disabled state) -- installing or removing a provider, or a provider bumping its own
    // Version (an algorithm/rule change), is the only thing that can make previously-generated aliases
    // stale; toggling enabled/disabled never does (see GetAllProviders). Compared against a snapshot's
    // stored AliasProvidersFingerprint on load (mirrors IndexerHelper.ComputeExclusionFingerprint) --
    // a mismatch means a forced recompaction is needed to regenerate every unique name's aliases.
    public static string ComputeProvidersFingerprint()
    {
        var entries = GetAllProviders()
            .Select(p => $"{GetComponentId(p)}:{p.Version}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(e => e, StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var entry in entries)
            sb.Append(entry).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    /// <summary>
    /// Detects whether a string contains any non-ASCII character. Ascii.IsValid is the BCL's
    /// vectorized scan, roughly twice as fast as a scalar loop on typical (mostly-ASCII) file
    /// names -- and this gate runs for every candidate on both the bake and live alias paths.
    /// </summary>
    public static bool HasNonAscii(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        return !Ascii.IsValid(text);
    }

    /// <summary>
    /// Detects unpaired surrogate halves. NTFS/Win32 file names are UTF-16 and CAN legally carry
    /// lone surrogates; every provider-side Unicode API (string.Normalize via the Windows
    /// NormalizeString, per-char code point conversion) throws on them, so alias generation skips
    /// such names outright instead of failing provider by provider on every scan.
    /// </summary>
    public static bool HasInvalidUtf16(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsSurrogate(text[i]))
                continue;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++; // a valid pair
                continue;
            }
            return true; // lone surrogate (either kind)
        }

        return false;
    }
}
