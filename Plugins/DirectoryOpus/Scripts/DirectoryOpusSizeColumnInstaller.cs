using System.IO;
using System.Reflection;
using System.Text;
using Lertaro.PluginSdk;

namespace Lertaro.Plugins.DirectoryOpus.Scripts;

/// <summary>Installs, updates or removes Lertaro's own Directory Opus script, following the plugin's size-column setting.</summary>
internal static class DirectoryOpusSizeColumnInstaller
{
    private const string ScriptFileName = "LertaroSize.js";
    private const string LabelKey = "Plugins_DirectoryOpus_SizeColumn_Label";
    private const string DescriptionKey = "Plugins_DirectoryOpus_SizeColumn_ScriptDescription";

    // The template's own first line: this is what makes a deployed file Lertaro's to delete.
    private const string ManagedMarker = "// Lertaro Directory Opus script integration. This file is managed by Lertaro.";

    public static void Sync(Assembly assembly, IReadOnlyDictionary<string, string> translations, bool enabled)
    {
        try
        {
            var directoryOpusDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GPSoftware", "Directory Opus");
            var scriptDirectory = Path.Combine(directoryOpusDataDirectory, "Script AddIns");
            var outputPath = Path.Combine(scriptDirectory, ScriptFileName);
            if (!Directory.Exists(directoryOpusDataDirectory))
                return;

            // An off position that only stops UPDATING leaves the column -- and the synchronous size query it
            // runs inside Opus's own process -- live. A setting has to take the code out of the host process.
            if (!enabled)
            {
                if (File.Exists(outputPath) && IsManagedByLertaro(File.ReadAllText(outputPath)))
                    File.Delete(outputPath);
                return;
            }

            var cliPath = Path.Combine(AppContext.BaseDirectory, "lff.exe");
            if (!File.Exists(cliPath))
                return;

            Directory.CreateDirectory(scriptDirectory);

            var label = GetTranslation(translations, LabelKey);
            var description = GetTranslation(translations, DescriptionKey);
            var content = DirectoryOpusSizeColumnScriptBuilder.Build(assembly, label, description, cliPath);
            if (File.Exists(outputPath) && File.ReadAllText(outputPath) == content)
                return;

            File.WriteAllText(outputPath, content, new UTF8Encoding(false));
            Logger.Log($"[DirectoryOpus] Updated the Lertaro Size column script at '{outputPath}'.", LogLevel.Debug);
        }
        catch (Exception ex)
        {
            // Warn, not Debug: this is why a user's Opus can end up running no column, no update, or an
            // outdated one with nothing in the default-level log to say so.
            Logger.Log($"[DirectoryOpus] Could not sync the Lertaro Size column script: {ex.Message}", LogLevel.Warn);
        }
    }

    internal static bool IsManagedByLertaro(string content) =>
        content.TrimStart('\uFEFF').StartsWith(ManagedMarker, StringComparison.Ordinal);

    private static string GetTranslation(IReadOnlyDictionary<string, string> translations, string key) =>
        translations.TryGetValue(key, out var value) ? value : key;
}
