namespace Lertaro.Core;

internal sealed class FileRecordNamePool
{
    private readonly object _lock = new();
    private readonly Dictionary<string, string> _pool = new(StringComparer.Ordinal);

    public string Get(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        lock (_lock)
        {
            if (_pool.TryGetValue(value, out var pooled))
                return pooled;

            _pool[value] = value;
            return value;
        }
    }
}

public static class FileRecordStoreSerializer
{
    public static string GetBasePath(string cacheDir, string sourceKey) => Path.Combine(cacheDir, sourceKey.ToLowerInvariant());
}
