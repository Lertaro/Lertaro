using System.Collections.Concurrent;
using Microsoft.Win32;

namespace Lertaro.Plugins.CoreExtensions.Preview.Handlers;

// Detects the Windows Preview Handler (IPreviewHandler) registered for a file extension, if any.
internal static class PreviewHandlerRegistry
{
    private const string ShellExPreview = @"ShellEx\{8895b1c6-b41f-4c1c-a562-0d564250836f}";
    private static readonly ConcurrentDictionary<string, Guid?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the preview-handler CLSID registered for the extension, or null if none.</summary>
    public static Guid? FindHandlerClsid(string ext)
    {
        if (string.IsNullOrEmpty(ext)) return null;
        if (ext[0] != '.') ext = "." + ext;
        return Cache.GetOrAdd(ext, Resolve);
    }

    private static Guid? Resolve(string ext)
    {
        // 1. Directly under the extension key, then under SystemFileAssociations for the extension.
        var clsid = ReadClsid($@"{ext}\{ShellExPreview}")
                    ?? ReadClsid($@"SystemFileAssociations\{ext}\{ShellExPreview}");
        if (clsid != null) return clsid;

        using var extKey = Registry.ClassesRoot.OpenSubKey(ext);

        // 2. Resolve the ProgID and look under it.
        if (extKey?.GetValue(null) is string progId && !string.IsNullOrEmpty(progId))
        {
            clsid = ReadClsid($@"{progId}\{ShellExPreview}");
            if (clsid != null) return clsid;
        }

        // 3. Fall back to the perceived type (e.g. SystemFileAssociations\document).
        if (extKey?.GetValue("PerceivedType") is string perceived && !string.IsNullOrEmpty(perceived))
            clsid = ReadClsid($@"SystemFileAssociations\{perceived}\{ShellExPreview}");

        return clsid;
    }

    private static Guid? ReadClsid(string subKeyPath)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(subKeyPath);
            if (key?.GetValue(null) is string s && Guid.TryParse(s, out var g))
                return g;
        }
        catch
        {
            // Registry access is best-effort.
        }
        return null;
    }
}
