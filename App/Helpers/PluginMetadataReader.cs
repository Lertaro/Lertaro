using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Lertaro.Core;

namespace Lertaro.App.Helpers;

// AssemblyMetadata attributes declare Lertaro.Plugin.Name/Description (literal text) or
// Lertaro.Plugin.NameKey/DescriptionKey (embedded translation keys). PEReader reads data only:
// no Assembly.Load, attribute construction, plugin getters or module initializers are invoked.
internal static class PluginMetadataReader
{
    internal sealed record Metadata(string Name, string Version, string Description, string SdkVersion);

    internal static Metadata Read(string path, string culture)
    {
        var fallback = new Metadata(Path.GetFileNameWithoutExtension(path), "", "", "");
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var pe = new PEReader(input);
            var reader = pe.GetMetadataReader();
            var assembly = reader.GetAssemblyDefinition();
            var values = ReadAttributes(reader, assembly);
            var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var language in new[] { "en-US", culture }.Distinct(StringComparer.OrdinalIgnoreCase))
                ReadTranslations(pe, reader, language, translations);
            string Resolve(string field, string defaultValue)
            {
                if (values.TryGetValue("Lertaro.Plugin." + field + "Key", out var key) && translations.TryGetValue(key, out var text)) return text;
                return values.GetValueOrDefault("Lertaro.Plugin." + field, defaultValue);
            }
            var sdk = reader.AssemblyReferences.Select(reader.GetAssemblyReference)
                .Where(reference => reader.GetString(reference.Name) is "Lertaro.PluginSdk" or "PluginSdk")
                .Select(reference => reference.Version.ToString(3)).FirstOrDefault() ?? "";
            return new(Resolve("Name", values.GetValueOrDefault("AssemblyTitleAttribute", fallback.Name)),
                assembly.Version.ToString(3), Resolve("Description", values.GetValueOrDefault("AssemblyDescriptionAttribute", "")),
                sdk);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or JsonException or InvalidOperationException or ArgumentException or OverflowException)
        {
            Logger.Log($"[PluginMetadata] Could not read '{path}': {ex.Message}", LogLevel.Warn);
            return fallback;
        }
    }

    private static Dictionary<string, string> ReadAttributes(MetadataReader reader, AssemblyDefinition assembly)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var handle in assembly.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            var constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference) continue;
            var type = reader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            if (reader.GetString(type.Namespace) != "System.Reflection") continue;
            var name = reader.GetString(type.Name);
            if (name is not ("AssemblyMetadataAttribute" or "AssemblyTitleAttribute" or "AssemblyDescriptionAttribute")) continue;
            var blob = reader.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1) throw new BadImageFormatException("Invalid metadata attribute.");
            var key = name == "AssemblyMetadataAttribute" ? blob.ReadSerializedString() : name;
            var value = blob.ReadSerializedString();
            if (key != null && value != null) result[key] = value;
        }
        return result;
    }

    private static void ReadTranslations(PEReader pe, MetadataReader reader, string culture, Dictionary<string, string> target)
    {
        var directory = pe.PEHeaders.CorHeader!.ResourcesDirectory;
        foreach (var handle in reader.ManifestResources)
        {
            var resource = reader.GetManifestResource(handle);
            var name = reader.GetString(resource.Name);
            if (!resource.Implementation.IsNil || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || !(name.Contains(".Resources.Translations." + culture + ".", StringComparison.OrdinalIgnoreCase)
                    || name.Contains(".Resources.Translations." + culture.Replace('-', '_') + ".", StringComparison.OrdinalIgnoreCase))) continue;
            var offset = checked((int)resource.Offset);
            if (offset < 0 || offset > directory.Size - 4) throw new BadImageFormatException("Invalid resource offset.");
            var data = pe.GetSectionData(directory.RelativeVirtualAddress).GetReader(offset, directory.Size - offset);
            var length = data.ReadInt32();
            if (length < 0 || length > data.RemainingBytes || length > 16 * 1024 * 1024) throw new BadImageFormatException("Invalid translation resource size.");
            ReadOnlySpan<byte> json = data.ReadBytes(length);
            if (json.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) json = json[3..];
            var translations = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (translations == null) continue;
            foreach (var pair in translations)
                if (!string.IsNullOrWhiteSpace(pair.Value)) target[pair.Key] = pair.Value;
        }
    }
}
