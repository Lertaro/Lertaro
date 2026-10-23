using System.Diagnostics;
using System.IO;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.CoreExtensions.Providers.Indexing;

/// <summary>Builds the bounded aliases cached with Start Menu and AppsFolder entries.</summary>
internal static class StartMenuAppAliasBuilder
{
    internal readonly record struct FileMetadata(
        string? ProductName,
        string? FileDescription,
        string? OriginalFilename);

    internal static SearchableItemAlias[] FromShortcut(string title, string targetPath)
    {
        var metadata = ReadMetadata(targetPath);
        return FromShortcut(title, targetPath, metadata);
    }

    internal static SearchableItemAlias[] FromShortcut(
        string title,
        string targetPath,
        FileMetadata metadata)
    {
        var aliases = new List<SearchableItemAlias>(4);
        if (Path.GetExtension(targetPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            Add(aliases, title, Path.GetFileNameWithoutExtension(targetPath),
                SearchableItemAliasKind.ShortcutTarget);
        }

        Add(aliases, title, metadata.ProductName, SearchableItemAliasKind.ProductMetadata);
        Add(aliases, title, metadata.FileDescription, SearchableItemAliasKind.ProductMetadata);
        Add(aliases, title, Path.GetFileNameWithoutExtension(metadata.OriginalFilename),
            SearchableItemAliasKind.ProductMetadata);
        return aliases.ToArray();
    }

    internal static SearchableItemAlias[] FromPackage(string title, string appUserModelId)
    {
        var aliases = new List<SearchableItemAlias>(2);
        if (string.IsNullOrWhiteSpace(appUserModelId) || LooksLikePath(appUserModelId))
            return [];

        var packageFamily = appUserModelId.Split('!', 2)[0];
        var publisher = packageFamily.IndexOf('_');
        var packageName = publisher > 0 ? packageFamily[..publisher] : packageFamily;
        Add(aliases, title, packageName, SearchableItemAliasKind.ProductMetadata);

        var lastDot = packageName.LastIndexOf('.');
        if (lastDot >= 0 && lastDot + 1 < packageName.Length)
            Add(aliases, title, packageName[(lastDot + 1)..], SearchableItemAliasKind.ProductMetadata);
        return aliases.ToArray();
    }

    private static FileMetadata ReadMetadata(string targetPath)
    {
        if (!Path.GetExtension(targetPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return default;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(targetPath);
            return new FileMetadata(info.ProductName, info.FileDescription, info.OriginalFilename);
        }
        catch
        {
            return default;
        }
    }

    private static void Add(
        List<SearchableItemAlias> aliases,
        string title,
        string? text,
        SearchableItemAliasKind kind)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value)
            || value.Length > 128
            || value.Equals(title.Trim(), StringComparison.OrdinalIgnoreCase)
            || !value.Any(char.IsLetterOrDigit)
            || aliases.Any(alias => alias.Text.Equals(value, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        aliases.Add(new SearchableItemAlias(value, kind));
    }

    private static bool LooksLikePath(string value) =>
        (value.Length > 2 && value[1] == ':') || value.StartsWith("\\\\", StringComparison.Ordinal);
}
