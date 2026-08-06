using System.IO;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.Indexing;

public class FileSizeColumnProvider : IResultColumnProvider
{
    public IEnumerable<ResultColumnDefinition> GetColumns() => new[]
        {
            new ResultColumnDefinition
            {
                ColumnId = "FileSize",
                HeaderText = TranslationService.Get("Column_HeaderSize"),
                Width = 100,
                // Without this, sorting falls back to comparing the formatted display strings
                // ("1.06 KB" vs "1 MB") as plain text, which ignores the unit entirely. Compare the
                // raw byte count instead.
                SortComparer = (a, b) => a.Metadata.Size.CompareTo(b.Metadata.Size)
            },
            new ResultColumnDefinition
            {
                ColumnId = "Extension",
                HeaderText = TranslationService.Get("Column_HeaderType"),
                Width = 80
            }
        };

    public string GetCellValue(ISearchResult result, string columnId)
    {
        if (result.IsDir)
        {
            return columnId == "Extension" ? TranslationService.Get("Column_TypeFolder") : string.Empty;
        }

        if (columnId == "FileSize")
        {
            // Already known from the index via ISearchResult.Metadata -- no per-cell disk I/O.
            // Metadata.Modified == DateTime.MinValue means this result isn't file-index-backed (e.g.
            // a plugin-provided item), matching the old fi.Exists == false -> empty case.
            return result.Metadata.Modified == DateTime.MinValue ? string.Empty : FormatSize(result.Metadata.Size);
        }

        if (columnId == "Extension")
        {
            var ext = Path.GetExtension(result.FullPath).ToUpper();
            return string.IsNullOrEmpty(ext)
                ? TranslationService.Get("Column_TypeFile")
                : TranslationService.Format("Column_TypeExtFile", ext.TrimStart('.'));
        }

        return string.Empty;
    }

    private static string FormatSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        double doubleBytes = bytes;
        var i = 0;
        while (doubleBytes >= 1024 && i < suffixes.Length - 1)
        {
            doubleBytes /= 1024;
            i++;
        }
        return $"{doubleBytes:0.##} {suffixes[i]}";
    }
}
