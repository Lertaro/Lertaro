using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Views.Controls;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;

namespace Lertaro.App.ViewModels.Settings.Plugins;

// A flat data file, never a plugin/script/resource package. Keep validation ahead of any UI mutation.
internal static class PluginConfigEntryTransfer
{
    internal const int MaxFileBytes = 1024 * 1024;
    private const string Format = "lertaro.plugin-config-entry";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static bool IsSupported(PluginConfigField field) => field.AllowEntryTransfer
        && field.FieldType == ConfigFieldType.Array && field.GetValue == null && field.SetValue == null
        && field.SubFields is { Count: > 0 } fields
        && fields.Select(f => f.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() == fields.Count
        && fields.All(f => !string.IsNullOrWhiteSpace(f.Key) && f.GetValue == null && f.SetValue == null
            && f.SubFields is not { Count: > 0 }
            && f.FieldType is ConfigFieldType.Boolean or ConfigFieldType.Text or ConfigFieldType.Hotkey
                or ConfigFieldType.FilePath or ConfigFieldType.FolderPath);

    internal static byte[] Export(string pluginId, string pluginVersion, PluginConfigField field, object? item)
    {
        CheckSchema(pluginId, pluginVersion, field);
        // JsonSerializer replaces unpaired UTF-16 surrogates; refuse them before it can lose data.
        if (item is IDictionary<string, object?> values)
        {
            try
            {
                foreach (var text in values.Values.OfType<string>()) _ = Utf8.GetByteCount(text);
            }
            catch (EncoderFallbackException ex)
            {
                throw new InvalidDataException(TranslationService.Get("Plugins_EntryInvalid"), ex);
            }
        }
        var value = JsonSerializer.SerializeToElement(item);
        var validated = ValidateItem(ReadObject(value), field, requireAll: true);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = Format,
            version = 1,
            pluginId,
            pluginVersion,
            settingKey = field.Key,
            fields = field.SubFields!.ToDictionary(f => f.Key, f => f.FieldType.ToString()),
            item = validated
        }, JsonOptions);
        CheckSize(bytes.Length);
        return bytes;
    }

    internal static Dictionary<string, object> Import(ReadOnlyMemory<byte> bytes, string pluginId,
        string pluginVersion, PluginConfigField field)
    {
        CheckSchema(pluginId, pluginVersion, field);
        CheckSize(bytes.Length);
        try
        {
            // Accept an optional UTF-8 BOM, but never silently replace bad bytes or guess ANSI/UTF-16.
            if (bytes.Span.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) bytes = bytes[3..];
            _ = Utf8.GetCharCount(bytes.Span);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var root = ReadObject(document.RootElement);
            string[] envelope = ["format", "version", "pluginId", "pluginVersion", "settingKey", "fields", "item"];
            if (root.Count != envelope.Length || envelope.Any(key => !root.ContainsKey(key)))
                throw Error("Invalid");
            if (ReadString(root["format"]) != Format) throw Error("UnsupportedFormat");
            if (!root["version"].TryGetInt32(out var version)) throw Error("Invalid");
            if (version != 1) throw Error("FormatVersion", version);
            var sourcePlugin = ReadString(root["pluginId"]);
            if (sourcePlugin != pluginId) throw Error("PluginMismatch", sourcePlugin, pluginId);
            var sourceSetting = ReadString(root["settingKey"]);
            if (sourceSetting != field.Key) throw Error("SettingMismatch", sourceSetting, field.Key);
            var sourceVersion = ReadString(root["pluginVersion"]);
            if (string.IsNullOrWhiteSpace(sourceVersion)) throw Error("Invalid");

            var fields = ReadObject(root["fields"]);
            var item = ReadObject(root["item"]);
            if (fields.Count != field.SubFields!.Count || field.SubFields.Any(f =>
                !fields.TryGetValue(f.Key, out var type) || ReadString(type) != f.FieldType.ToString())
                || (sourceVersion != pluginVersion && field.SubFields.Any(f => !item.ContainsKey(f.Key))))
                throw sourceVersion != pluginVersion
                    ? Error("VersionIncompatible", sourceVersion, pluginVersion) : Error("Incompatible");

            // Same-version hand-written files may omit values and use current defaults. A different
            // version must supply every field; there is no migration or lossy best-effort import.
            return ValidateItem(item, field, requireAll: false);
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or EncoderFallbackException or InvalidOperationException)
        {
            throw new InvalidDataException(TranslationService.Get("Plugins_EntryInvalid"), ex);
        }
    }

    private static Dictionary<string, object> ValidateItem(Dictionary<string, JsonElement> properties, PluginConfigField field, bool requireAll)
    {
        foreach (var key in properties.Keys)
            if (!field.SubFields!.Any(f => f.Key == key)) throw Error("UnknownField", key);

        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in field.SubFields!)
        {
            if (requireAll && !properties.ContainsKey(child.Key)) throw Error("MissingField", child.Key);
            var value = properties.TryGetValue(child.Key, out var supplied)
                ? supplied : JsonSerializer.SerializeToElement(child.DefaultValue);
            if (child.FieldType == ConfigFieldType.Boolean)
            {
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw InvalidField(child);
                result.Add(child.Key, value.GetBoolean());
                continue;
            }
            if (value.ValueKind != JsonValueKind.String) throw InvalidField(child);
            var text = ReadString(value);
            if (child.MaxLength > 0 && text.Length > child.MaxLength) throw Error("TooLong", child.Key, child.MaxLength);
            if (child.RequireNonEmpty && string.IsNullOrWhiteSpace(text)) throw Error("Required", child.Key);
            if (text.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                throw Error("EmbeddedResource", child.Key);
            // The icon editor already stores SVG as Path Data; validate and preserve it verbatim.
            if (child.Key.Equals("Icon", StringComparison.OrdinalIgnoreCase) && !SvgIconInputHelper.IsValidPathData(text))
                throw Error("InvalidIcon", child.Key);
            if (child.FieldType == ConfigFieldType.Hotkey && text.Length != 0 && !IsValidHotkey(text, child.RequireModifier))
                throw Error("InvalidHotkey", child.Key);
            result.Add(child.Key, text);
        }
        return result;
    }

    private static bool IsValidHotkey(string text, bool requireModifier)
    {
        var parts = text.Split('+');
        var modifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts[..^1])
        {
            var modifier = part.Trim().ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => "Ctrl", "ALT" => "Alt", "SHIFT" => "Shift",
                "WIN" or "WINDOWS" => "Win", _ => ""
            };
            if (modifier.Length == 0 || !modifiers.Add(modifier)) return false;
        }
        // Enum.TryParse accepts numeric ordinals and comma lists which the recorder never emits.
        return Enum.GetNames<Key>().Contains(parts[^1].Trim(), StringComparer.OrdinalIgnoreCase)
            && WpfUiHelper.TryParseHotkey(text, out var key, out var keys)
            && key is not (Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed or Key.Escape or Key.Clear or Key.OemClear)
            && HotkeyRecorderModifierState.FromKey(key) == ModifierKeys.None
            && (!requireModifier || keys != ModifierKeys.None)
            && !HotkeyStringFormat.IsReservedWindowsShortcut(text);
    }

    private static Dictionary<string, JsonElement> ReadObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Error("Invalid");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw Error("Invalid");
            properties.Add(property.Name, property.Value);
        }
        return properties;
    }

    private static string ReadString(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) throw Error("Invalid");
        var text = value.GetString()!;
        _ = Utf8.GetByteCount(text);
        return text;
    }

    private static void CheckSchema(string pluginId, string pluginVersion, PluginConfigField field)
    {
        if (!IsSupported(field) || string.IsNullOrWhiteSpace(pluginId) || string.IsNullOrWhiteSpace(pluginVersion))
            throw Error("Unavailable", string.Empty);
    }

    private static void CheckSize(long size)
    {
        if (size > MaxFileBytes) throw Error("TooLarge");
    }

    private static InvalidDataException Error(string reason, params object[] args) => new(TranslationService.Format("Plugins_Entry" + reason, args));
    private static InvalidDataException InvalidField(PluginConfigField field) =>
        new(string.Format(TranslationService.Get("Plugins_EntryInvalidField"), field.Key));

    internal static string ErrorMessage(Exception error)
    {
        if (error is InvalidDataException) return error.Message;
        var reason = error switch
        {
            FileNotFoundException => "FileNotFound",
            DirectoryNotFoundException => "DirectoryNotFound",
            PathTooLongException or ArgumentException or NotSupportedException => "InvalidPath",
            UnauthorizedAccessException => "AccessDenied",
            IOException when (error.HResult & 0xffff) is 32 or 33 => "FileInUse",
            IOException when (error.HResult & 0xffff) is 39 or 112 => "DiskFull",
            InvalidOperationException => "Unavailable",
            _ => "IoError"
        };
        return TranslationService.Format("Plugins_Entry" + reason, error.Message);
    }

    internal static byte[] ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        CheckSize(stream.Length);
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    internal static void WriteFile(string path, byte[] bytes)
    {
        CheckSize(bytes.Length);
        var target = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, $".entry-{Guid.NewGuid():N}.tmp");
        var created = false;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (created)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    internal static string SuggestedFileName(PluginConfigArrayItemViewModel item)
    {
        var title = new[] { "Title", "Name", "Keyword" }.Select(key => item.Children
            .FirstOrDefault(f => f.SchemaField.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value as string)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "entry";
        var name = string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        if (name.Length > 100) name = name[..(char.IsHighSurrogate(name[99]) ? 99 : 100)].TrimEnd(' ', '.');
        if (name.Length == 0) name = "entry";
        if (Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            name = "entry-" + name;
        return name + ".json";
    }
}
