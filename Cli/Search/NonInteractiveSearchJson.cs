using System.Text.Json;
using Lertaro.Core;

namespace Lertaro.Cli.Search;

internal static class NonInteractiveSearchJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Serialize(IEnumerable<SearchResult> results) => JsonSerializer.Serialize(
        results.Select(result => new
        {
            result.Name,
            result.Path,
            IsDirectory = result.IsDir,
            result.Drive,
            result.Attributes,
            Size = result.Metadata.Size,
            Created = result.Metadata.Created,
            Modified = result.Metadata.Modified,
            Accessed = result.Metadata.Accessed
        }), SerializerOptions);
}
