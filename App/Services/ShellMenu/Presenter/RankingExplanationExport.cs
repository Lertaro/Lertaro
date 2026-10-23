using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using Lertaro.Core.SearchIndex;

namespace Lertaro.App.Services.ShellMenu.Presenter;

/// <summary>
/// Exports the ranking explanation of every displayed quick-window result to one plaintext file.
/// The single-row Copy action remains the primary path; this is for bug reports that need the whole page.
/// </summary>
internal static class RankingExplanationExport
{
    internal static string BuildText(IReadOnlyList<AppSearchResult> results, string rawQuery)
    {
        var blocks = new List<string>(results.Count + 1)
        {
            // Same warning as the single-row copy: this text is deliberately not hashed.
            "# plaintext: the query, display names and paths below are written as typed",
            "# Lertaro quick-window ranking details",
            $"window=quick",
            $"query={JsonSerializer.Serialize(rawQuery)}",
            $"resultCount={results.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
        };

        foreach (var result in results)
        {
            blocks.Add("---");
            if (result.RankingExplanation is { } explanation)
            {
                blocks.Add(RankingExplanationFormatter.Format(
                    explanation,
                    string.IsNullOrEmpty(result.SearchQuery) ? rawQuery : result.SearchQuery,
                    result.Name,
                    result.FullPath));
            }
            else
            {
                blocks.Add($"result.name={JsonSerializer.Serialize(result.Name)}");
                blocks.Add($"result.path={JsonSerializer.Serialize(result.FullPath)}");
                blocks.Add("rank.kernel=unavailable");
            }
        }

        return string.Join('\n', blocks) + "\n";
    }

    internal static void Export(IReadOnlyList<AppSearchResult> results, string rawQuery, Window? owner)
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = TranslationManager.Instance["Action_ExportRankingDetails"],
                Filter = "Text (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = ".txt",
                FileName = $"ranking-details-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            };
            if (dialog.ShowDialog(owner) != true)
                return;

            File.WriteAllText(dialog.FileName, BuildText(results, rawQuery), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex)
        {
            Core.Logger.Log($"[RankingTrace] Failed to export ranking explanations: {ex.Message}", Core.LogLevel.Warn);
        }
    }
}
