using System.IO;
using System.Text.RegularExpressions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.InstantAnswers;

public class EnvironmentVariableInstantProvider : IInstantResultProvider
{
    public string Name => TranslationService.Get("Env_Name");

    // Matches %VARIABLE_NAME% pattern
    private static readonly Regex EnvVarRegex = new Regex(@"%[a-zA-Z0-9_]+%", RegexOptions.Compiled);

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            yield break;

        var trimmed = query.Trim();

        // 1. Fuzzy matching mode: starts with '%' and is either just '%' or a single unclosed/closed variable name
        var firstPercentIdx = trimmed.IndexOf('%');
        var lastPercentIdx = trimmed.LastIndexOf('%');

        var isFuzzyMode = firstPercentIdx == 0 && (lastPercentIdx == 0 || (lastPercentIdx == trimmed.Length - 1 && trimmed.Length > 1));

        if (isFuzzyMode && trimmed.Length >= 3 && trimmed.EndsWith("%"))
        {
            var varName = trimmed.Substring(1, trimmed.Length - 2);
            if (!string.IsNullOrEmpty(varName) && Environment.GetEnvironmentVariable(varName) != null)
            {
                isFuzzyMode = false;
            }
        }

        if (isFuzzyMode)
        {
            // Extract the search term (e.g., "%TEMP%" -> "TEMP", "%TE" -> "TE", "%" -> "")
            var searchTerm = trimmed.Substring(1);
            if (searchTerm.EndsWith("%"))
            {
                searchTerm = searchTerm.Substring(0, searchTerm.Length - 1);
            }

            var envs = Environment.GetEnvironmentVariables();
            var prefixMatches = new List<(string Name, string Value)>();
            var containsMatches = new List<(string Name, string Value)>();

            foreach (System.Collections.DictionaryEntry entry in envs)
            {
                var name = entry.Key?.ToString();
                var val = entry.Value?.ToString();
                if (string.IsNullOrEmpty(name) || val == null)
                    continue;

                if (string.IsNullOrEmpty(searchTerm))
                {
                    prefixMatches.Add((name, val));
                }
                else if (name.StartsWith(searchTerm, StringComparison.OrdinalIgnoreCase))
                {
                    prefixMatches.Add((name, val));
                }
                else if (name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                {
                    containsMatches.Add((name, val));
                }
            }

            // Sort alphabetically
            prefixMatches.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            containsMatches.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            var results = prefixMatches.Concat(containsMatches).Take(15);

            foreach (var (name, val) in results)
            {
                var varName = $"%{name}%";
                var isDir = false;
                var isFile = false;
                var exists = false;

                if (IsLocalFixedPath(val))
                {
                    isDir = Directory.Exists(val);
                    isFile = !isDir && File.Exists(val);
                    exists = isDir || isFile;
                }

                var typeDesc = isDir
                    ? TranslationService.Get("Column_TypeFolder")
                    : (isFile ? TranslationService.Get("Column_TypeFile") : TranslationService.Get("Env_PathNotExist"));

                yield return new InstantResultItem
                {
                    Title = varName,
                    Description = exists
                        ? $"{val} ({typeDesc})"
                        : $"{val}",
                    IconData = exists
                        ? "M20 6h-8l-2-2H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2z" // Folder icon
                        : "M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-2 10H7v-2h10v2z", // Variable icon
                    IconColor = exists ? "AccentBlue" : "TextSecondary",
                    ActionType = exists ? "Execute" : "Copy",
                    ActionArgument = val,
                    TabCompletion = varName
                };
            }

            yield break;
        }

        // 2. Existing path expansion logic
        if (!EnvVarRegex.IsMatch(trimmed))
            yield break;

        string expanded;
        try
        {
            expanded = Environment.ExpandEnvironmentVariables(trimmed);
        }
        catch
        {
            yield break;
        }

        // If the expanded text is identical to input or still contains unexpanded % characters, it's invalid
        if (string.Equals(trimmed, expanded, StringComparison.OrdinalIgnoreCase) || expanded.Contains("%"))
            yield break;

        // Handle multi-path variables (like %PATH%, %PATHEXT%, %PSMODULEPATH%) which contain semicolons
        if (expanded.Contains(";"))
        {
            var paths = expanded.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var path in paths)
            {
                var cleanedPath = path.Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(cleanedPath))
                    continue;

                var partIsDir = Directory.Exists(cleanedPath);
                var partIsFile = File.Exists(cleanedPath);
                var partExists = partIsDir || partIsFile;
                var partTypeDesc = partIsDir
                    ? TranslationService.Get("Column_TypeFolder")
                    : (partIsFile ? TranslationService.Get("Column_TypeFile") : TranslationService.Get("Env_PathNotExist"));

                yield return new InstantResultItem
                {
                    Title = cleanedPath,
                    Description = partExists
                        ? TranslationService.Format("Env_SegmentOpenHint", partTypeDesc)
                        : TranslationService.Format("Env_SegmentCopyHint", partTypeDesc),
                    IconData = partExists
                        ? "M20 6h-8l-2-2H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2z" // Folder icon
                        : "M16 1H4c-1.1 0-2 .9-2 2v14h2V3h12V1zm3 4H8c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h11c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2zm0 16H8V7h11v14z", // Copy icon
                    IconColor = partExists ? "AccentBlue" : "TextSecondary",
                    ActionType = partExists ? "Execute" : "Copy",
                    ActionArgument = cleanedPath,
                    TabCompletion = cleanedPath
                };
            }
        }
        else
        {
            // Single path variable (like %APPDATA%, %TEMP%)
            var isDir = Directory.Exists(expanded);
            var isFile = File.Exists(expanded);
            var exists = isDir || isFile;
            var typeDesc = isDir
                ? TranslationService.Get("Column_TypeFolder")
                : (isFile ? TranslationService.Get("Column_TypeFile") : TranslationService.Get("Env_PathNotExist"));

            yield return new InstantResultItem
            {
                Title = expanded,
                Description = exists
                    ? TranslationService.Format("Env_ExpandOpenHint", typeDesc)
                    : TranslationService.Format("Env_ExpandCopyHint", typeDesc),
                IconData = exists
                    ? "M20 6h-8l-2-2H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2z" // Folder icon
                    : "M16 1H4c-1.1 0-2 .9-2 2v14h2V3h12V1zm3 4H8c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h11c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2zm0 16H8V7h11v14z", // Copy icon
                IconColor = exists ? "AccentBlue" : "TextSecondary",
                ActionType = exists ? "Execute" : "Copy",
                ActionArgument = expanded,
                TabCompletion = expanded
            };
        }
    }

    public bool[]? GetHighlightMask(string text, string query)
    {
        if (string.IsNullOrEmpty(query)) return null;
        var trimmed = query.Trim();

        var mask = new bool[text.Length];
        if (text.StartsWith("%") && text.EndsWith("%") && trimmed.StartsWith("%"))
        {
            var searchTerm = trimmed.Substring(1);
            if (searchTerm.EndsWith("%"))
            {
                searchTerm = searchTerm.Substring(0, searchTerm.Length - 1);
            }

            if (string.IsNullOrEmpty(searchTerm)) return mask;

            return FuzzyMatchService.GetHighlightMask(text, searchTerm) ?? mask;
        }
        else
        {
            return mask;
        }
    }

    private static bool IsLocalFixedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 3) return false;
        if (path[1] != ':' || path[2] != '\\') return false;
        // Invariant to avoid the Turkish-I problem: 'i'.ToUpper() yields U+0130 in tr-TR, failing the A-Z check.
        var driveLetter = char.ToUpperInvariant(path[0]);
        if (driveLetter < 'A' || driveLetter > 'Z') return false;

        try
        {
            var drive = new DriveInfo(driveLetter.ToString());
            return drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Ram;
        }
        catch
        {
            return false;
        }
    }
}
