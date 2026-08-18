using System.IO;
using System.Reflection;
using System.Text;
using Lertaro.PluginSdk;

namespace Lertaro.Plugins.DirectoryOpus.Scripts;

/// <summary>Deploys Lertaro's own Directory Opus script only when Directory Opus and lff are both present.</summary>
internal static class DirectoryOpusSizeColumnInstaller
{
    private const string ScriptFileName = "LertaroSize.js";
    private const string LabelKey = "Plugins_DirectoryOpus_SizeColumn_Label";
    private const string DescriptionKey = "Plugins_DirectoryOpus_SizeColumn_ScriptDescription";

    public static void EnsureInstalled(Assembly assembly, IReadOnlyDictionary<string, string> translations)
    {
        try
        {
            var directoryOpusDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GPSoftware", "Directory Opus");
            var scriptDirectory = Path.Combine(directoryOpusDataDirectory, "Script AddIns");
            var cliPath = Path.Combine(AppContext.BaseDirectory, "lff.exe");
            if (!Directory.Exists(directoryOpusDataDirectory) || !File.Exists(cliPath))
                return;

            Directory.CreateDirectory(scriptDirectory);

            var label = GetTranslation(translations, LabelKey);
            var description = GetTranslation(translations, DescriptionKey);
            var content = DirectoryOpusSizeColumnScriptBuilder.Build(assembly, label, description, cliPath);
            var outputPath = Path.Combine(scriptDirectory, ScriptFileName);
            if (File.Exists(outputPath) && File.ReadAllText(outputPath) == content)
                return;

            File.WriteAllText(outputPath, content, new UTF8Encoding(false));
            Logger.Log($"[DirectoryOpus] Updated the Lertaro Size column script at '{outputPath}'.", LogLevel.Debug);
        }
        catch (Exception ex)
        {
            Logger.Log($"[DirectoryOpus] Could not deploy the Lertaro Size column script: {ex.Message}", LogLevel.Debug);
        }
    }

    private static string GetTranslation(IReadOnlyDictionary<string, string> translations, string key) =>
        translations.TryGetValue(key, out var value) ? value : key;
}
