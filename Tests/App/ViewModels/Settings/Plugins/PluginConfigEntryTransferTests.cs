using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.CustomActions;
using Lertaro.Plugins.CustomCommands;
using Lertaro.Plugins.WebSearch;

namespace Lertaro.App.Tests.ViewModels.Settings.Plugins;

[TestClass]
public sealed class PluginConfigEntryTransferTests
{
    private const string ComplexText = "中文 😀 café\r\n\t\"quoted\" \\server\\目录\\程序.exe %s %1 ${name} ` & < >";

    private static PluginConfigFieldViewModel Field(bool actions = false)
    {
        IConfigurable plugin = actions ? new CustomActionsPlugin() : new CustomCommandsPlugin();
        var assembly = plugin.GetType().Assembly.GetName();
        return new(assembly.Name!, plugin.GetConfigSchema().Fields.Single(), new UserSettings(),
            pluginVersion: assembly.Version!.ToString(3));
    }

    private static byte[] Entry(PluginConfigFieldViewModel field)
    {
        var item = field.SchemaField.SubFields!.ToDictionary(f => f.Key, f => f.DefaultValue);
        item["Title"] = ComplexText;
        item["Path"] = @"%ProgramFiles%\另一台机器\tool.exe";
        item["Parameter"] = ComplexText;
        item["WorkingDir"] = @"Z:\missing\folder";
        return PluginConfigEntryTransfer.Export(field.PluginId, field.PluginVersion, field.SchemaField, item);
    }

    private static byte[] Changed(PluginConfigFieldViewModel field, Action<JsonObject> change)
    {
        var root = JsonNode.Parse(Entry(field))!.AsObject();
        change(root);
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RoundTrip_RealPluginSchema_PreservesTextMetadataAndTypedLists(bool actions)
    {
        var field = Field(actions);
        var bytes = Entry(field);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        Assert.AreEqual(field.PluginVersion, root.GetProperty("pluginVersion").GetString());
        Assert.AreEqual(field.PluginId, root.GetProperty("pluginId").GetString());
        Assert.AreEqual(field.SchemaField.Key, root.GetProperty("settingKey").GetString());
        Assert.AreEqual(12, root.GetProperty("item").EnumerateObject().Count());
        Assert.AreEqual("FilePath", root.GetProperty("fields").GetProperty("Path").GetString());
        Assert.AreEqual("Boolean", root.GetProperty("fields").GetProperty("Enabled").GetString());
        Assert.IsFalse(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));

        field.ImportEntry(bytes);
        field.Commit();

        var stored = field.Settings.GetPluginSetting<object?>(field.PluginId, field.SchemaField.Key, null);
        var json = JsonSerializer.Serialize(stored);
        if (actions)
        {
            var item = Assert.ContainsSingle(JsonSerializer.Deserialize<List<DynamicActionProvider.ActionItem>>(json)!);
            Assert.AreEqual(ComplexText, item.Title);
            Assert.AreEqual(ComplexText, item.Parameter);
            Assert.IsTrue(item.Enabled);
        }
        else
        {
            var item = Assert.ContainsSingle(JsonSerializer.Deserialize<List<CustomCommandsInstantProvider.CommandItem>>(json)!);
            Assert.AreEqual(ComplexText, item.Title);
            Assert.AreEqual(ComplexText, item.Parameter);
            Assert.IsTrue(item.Enabled);
        }
    }

    [TestMethod]
    public void Import_SameVersionMissingValues_UsesDefaultsWithoutSharingRows()
    {
        var field = Field();
        var bytes = Changed(field, root => root["item"] = new JsonObject { ["Title"] = "old" });
        field.ImportEntry(bytes);
        field.ImportEntry(bytes);
        field.SelectedArrayItem!.Children.Single(c => c.SchemaField.Key == "Title").Value = "edited";

        Assert.HasCount(2, field.ArrayItems);
        Assert.AreEqual("old", field.ArrayItems[0].Children.Single(c => c.SchemaField.Key == "Title").Value);
        Assert.AreEqual(true, field.ArrayItems[1].Children.Single(c => c.SchemaField.Key == "Enabled").Value);
        Assert.AreEqual("", field.ArrayItems[1].Children.Single(c => c.SchemaField.Key == "Icon").Value);
    }

    [TestMethod]
    public void Import_DifferentVersionIdenticalFields_Accepts()
    {
        var field = Field();
        field.ImportEntry(Changed(field, root => root["pluginVersion"] = "999.0.0"));
        Assert.HasCount(1, field.ArrayItems);
    }

    [TestMethod]
    [DataRow("missingValue")]
    [DataRow("missingField")]
    [DataRow("extraField")]
    [DataRow("changedType")]
    public void Import_DifferentVersionChangedFields_RejectsWholeEntry(string change)
    {
        var field = Field();
        var bytes = Changed(field, root =>
        {
            root["pluginVersion"] = "999.0.0";
            switch (change)
            {
                case "missingValue": root["item"]!.AsObject().Remove("Title"); break;
                case "missingField": root["fields"]!.AsObject().Remove("Title"); break;
                case "extraField": root["fields"]!["New"] = "Text"; break;
                case "changedType": root["fields"]!["Path"] = "Text"; break;
            }
        });
        var error = Assert.ThrowsExactly<InvalidDataException>(() => field.ImportEntry(bytes));
        Assert.AreEqual(TranslationService.Format("Plugins_EntryVersionIncompatible", "999.0.0", field.PluginVersion), error.Message);
        Assert.IsEmpty(field.ArrayItems);
        Assert.IsFalse(field.IsDirty);
    }

    [TestMethod]
    [DataRow("format", "\"other\"")]
    [DataRow("version", "2")]
    [DataRow("version", "\"1\"")]
    [DataRow("pluginId", "\"Lertaro.Plugins.CustomActions\"")]
    [DataRow("settingKey", "\"Actions\"")]
    [DataRow("pluginVersion", "\"\"")]
    [DataRow("item", "[]")]
    [DataRow("fields", "[]")]
    [DataRow("resources", "{\"icon\":\"base64\"}")]
    public void Import_InvalidEnvelope_DoesNotChangeExistingRows(string key, string json)
    {
        var field = Field();
        field.ImportEntry(Entry(field));
        field.Commit();
        var selected = field.SelectedArrayItem;
        var before = JsonSerializer.Serialize(field.LocalValueStore);

        Assert.ThrowsExactly<InvalidDataException>(() => field.ImportEntry(Changed(field, root => root[key] = JsonNode.Parse(json))));

        Assert.HasCount(1, field.ArrayItems);
        Assert.AreSame(selected, field.SelectedArrayItem);
        Assert.AreEqual(before, JsonSerializer.Serialize(field.LocalValueStore));
        Assert.IsFalse(field.IsDirty);
    }

    [TestMethod]
    [DataRow("Enabled", "\"true\"")]
    [DataRow("Title", "17")]
    [DataRow("Title", "null")]
    [DataRow("Title", "{\"$type\":\"System.Object\"}")]
    [DataRow("TITLE", "\"alias\"")]
    [DataRow("Unknown", "true")]
    [DataRow("Icon", "\"not-a-path\"")]
    [DataRow("Icon", "\"<svg/>\"")]
    [DataRow("Icon", "\"data:image/png;base64,AAAA\"")]
    [DataRow("Path", "\"data:application/octet-stream;base64,AAAA\"")]
    [DataRow("Hotkey", "\"F9\"")]
    [DataRow("Hotkey", "\"Ctrl+999\"")]
    [DataRow("Hotkey", "\"Ctrl+A+B\"")]
    [DataRow("Hotkey", "\"Ctrl++A\"")]
    [DataRow("Hotkey", "\"garbage+Ctrl+A\"")]
    [DataRow("Hotkey", "\"Ctrl+Control+A\"")]
    [DataRow("Hotkey", "\"Ctrl+LeftShift\"")]
    [DataRow("Hotkey", "\"Win+R\"")]
    public void Import_InvalidItem_RejectsWithoutDirtyingList(string key, string json)
    {
        var field = Field(actions: true);
        Assert.ThrowsExactly<InvalidDataException>(() => field.ImportEntry(Changed(field, root => root["item"]![key] = JsonNode.Parse(json))));
        Assert.IsEmpty(field.ArrayItems);
        Assert.IsFalse(field.IsDirty);
    }

    [TestMethod]
    [DataRow("Ctrl+Alt+F9")]
    [DataRow("Control+Shift+Enter")]
    [DataRow("Ctrl+D1")]
    public void Import_RecorderHotkey_Accepts(string hotkey)
    {
        var field = Field(actions: true);
        field.ImportEntry(Changed(field, root => root["item"]!["Hotkey"] = hotkey));
        Assert.AreEqual(hotkey, field.SelectedArrayItem!.Children.Single(c => c.IsHotkey).Value);
    }

    [TestMethod]
    [DataRow("\"version\": 1", "\"version\": 1, \"Version\": 1")]
    [DataRow("\"Enabled\": true", "\"Enabled\": true, \"Enabled\": false")]
    [DataRow("\"Enabled\": true", "\"Enabled\": true, \"\\u0065nabled\": false")]
    [DataRow("\"Enabled\": \"Boolean\"", "\"Enabled\": \"Boolean\", \"enabled\": \"Boolean\"")]
    public void Import_DuplicatePropertiesIncludingEscapedAliases_Rejects(string original, string replacement)
    {
        var field = Field();
        var json = Encoding.UTF8.GetString(Entry(field));
        Assert.Contains(original, json);
        Assert.ThrowsExactly<InvalidDataException>(() => field.ImportEntry(Encoding.UTF8.GetBytes(json.Replace(original, replacement))));
    }

    [TestMethod]
    public void Import_BomAndLiteralUnicode_AcceptsButMalformedEncodingAndSurrogatesFail()
    {
        var field = Field();
        var bytes = Entry(field);
        field.ImportEntry(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(bytes).ToArray());
        Assert.ThrowsExactly<InvalidDataException>(() => field.ImportEntry(new byte[] { 0xFF }.Concat(bytes).ToArray()));
        Assert.ThrowsExactly<InvalidDataException>(() => field.ImportEntry(Encoding.Unicode.GetBytes(Encoding.UTF8.GetString(bytes))));
        var json = Encoding.UTF8.GetString(bytes).Replace("\"Icon\": \"\"", "\"Icon\": \"\\uD800\"");
        Assert.ThrowsExactly<InvalidDataException>(() => field.ImportEntry(Encoding.UTF8.GetBytes(json)));
    }

    [TestMethod]
    public void Import_LimitsAndTrailingDocument_Rejects()
    {
        var field = Field();
        Assert.ThrowsExactly<InvalidDataException>(() => field.ImportEntry(new byte[PluginConfigEntryTransfer.MaxFileBytes + 1]));
        Assert.ThrowsExactly<InvalidDataException>(() => field.ImportEntry(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Entry(field)) + "{}")));
        Assert.ThrowsExactly<InvalidDataException>(() => field.ImportEntry(Encoding.UTF8.GetBytes("{\"item\":" + new string('[', 10) + "0" + new string(']', 10) + "}")));
    }

    [TestMethod]
    public void ImportAndExport_StagedChangesSurviveSwitchAndCancelRestoresOriginal()
    {
        var field = Field();
        var plugin = new PluginInfoViewModel("Commands", field.PluginVersion, "Commands.dll", "1", [], [field]);
        plugin.IsConfigTab = true;
        field.ImportEntry(Entry(field));
        var selected = field.SelectedArrayItem;
        selected!.Children.Single(c => c.SchemaField.Key == "Title").Value = "未应用 \\ \"编辑\"";
        plugin.CloseConfigRowsForSelectionChange();
        plugin.IsConfigTab = true;

        Assert.AreSame(selected, field.SelectedArrayItem);
        using var exported = JsonDocument.Parse(field.ExportEntry());
        Assert.AreEqual("未应用 \\ \"编辑\"", exported.RootElement.GetProperty("item").GetProperty("Title").GetString());
        Assert.IsNull(field.Settings.GetPluginSetting<object?>(field.PluginId, field.SchemaField.Key, null));
        Assert.IsTrue(plugin.HasPendingConfigEdits);

        plugin.RollbackConfig();
        Assert.IsEmpty(field.ArrayItems);
        Assert.IsFalse(field.CanExportEntry);
        Assert.IsFalse(plugin.HasPendingConfigEdits);
    }

    [TestMethod]
    [DataRow(false, "Keyword", "example", "example")]
    [DataRow(false, "Keyword", "example", " EXAMPLE ")]
    [DataRow(true, "Hotkey", "Ctrl+Alt+F9", "Control+Alt+F9")]
    public void Import_DuplicateTrigger_AppendsSelectsAndWarns(bool actions, string key, string first, string second)
    {
        var field = Field(actions);
        field.ImportEntry(Changed(field, root => root["item"]![key] = first));
        var warning = field.ImportEntry(Changed(field, root => root["item"]![key] = second));
        Assert.HasCount(2, field.ArrayItems);
        Assert.AreSame(field.ArrayItems[1], field.SelectedArrayItem);
        Assert.IsNotEmpty(warning);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Export_EmbeddedIcon_PreservesStagedIconThroughImportAndCommit(bool actions)
    {
        var field = Field(actions);
        field.ImportEntry(Entry(field));
        var icon = field.SelectedArrayItem!.Children.Single(c => c.IsIconField);
        const string path = "F1 M0,0 L10,0 10,10 Z M2,2 A1,1 0 0 1 3,3";
        icon.Value = path;
        var bytes = field.ExportEntry();
        using var document = JsonDocument.Parse(bytes);
        Assert.AreEqual(path, document.RootElement.GetProperty("item").GetProperty("Icon").GetString());
        Assert.AreEqual(path, icon.Value);
        var imported = Field(actions);
        imported.ImportEntry(bytes);
        imported.Commit();
        var stored = JsonSerializer.SerializeToElement(imported.Settings.GetPluginSetting<object?>(imported.PluginId, imported.SchemaField.Key, null));
        Assert.AreEqual(path, stored[0].GetProperty("Icon").GetString());
    }

    [TestMethod]
    public void Export_InvalidIcon_ReportsReasonWithoutChangingStagedValue()
    {
        var field = Field();
        field.ImportEntry(Entry(field));
        var icon = field.SelectedArrayItem!.Children.Single(c => c.IsIconField);
        icon.Value = "invalid path";
        var error = Assert.ThrowsExactly<InvalidDataException>(() => field.ExportEntry());
        Assert.AreEqual(TranslationService.Format("Plugins_EntryInvalidIcon", "Icon"), error.Message);
        Assert.AreEqual("invalid path", icon.Value);
    }

    [TestMethod]
    public void RoundTrip_WebSearch_DefaultEnginesPreserveIconsAndUrls()
    {
        var plugin = new WebSearchPlugin();
        var assembly = plugin.GetType().Assembly.GetName();
        var field = new PluginConfigFieldViewModel(assembly.Name!, plugin.GetConfigSchema().Fields.Single(),
            new UserSettings(), pluginVersion: assembly.Version!.ToString(3));
        Assert.IsTrue(field.SupportsEntryTransfer);
        var count = field.ArrayItems.Count;
        var expected = new List<WebSearchInstantProvider.SearchSourceItem>();
        foreach (var row in field.ArrayItems.ToArray())
        {
            field.SelectedArrayItem = row;
            var source = JsonSerializer.Deserialize<WebSearchInstantProvider.SearchSourceItem>(JsonSerializer.Serialize(row.GetValue()))!;
            expected.Add(source);
            Assert.IsNotEmpty(source.Icon);
            Assert.AreEqual(source.Name + ".json", PluginConfigEntryTransfer.SuggestedFileName(row));
            Assert.IsNotEmpty(field.ImportEntry(field.ExportEntry())); // A duplicate keyword still warns.
            Assert.AreEqual(source.Icon, field.SelectedArrayItem!.IconField!.Value);
        }
        Assert.HasCount(count * 2, field.ArrayItems);
        field.Commit();
        var stored = JsonSerializer.Deserialize<List<WebSearchInstantProvider.SearchSourceItem>>(JsonSerializer.Serialize(
            field.Settings.GetPluginSetting<object?>(field.PluginId, field.SchemaField.Key, null)))!;
        Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(stored.Skip(count)));
    }

    [TestMethod]
    [DataRow("format", "\"other\"", "UnsupportedFormat")]
    [DataRow("version", "2", "FormatVersion")]
    [DataRow("version", "\"2\"", "Invalid")]
    [DataRow("pluginId", "\"other.plugin\"", "PluginMismatch")]
    [DataRow("settingKey", "\"OtherSettings\"", "SettingMismatch")]
    [DataRow("fields", "{}", "Incompatible")]
    [DataRow("item", "[]", "Invalid")]
    public void Import_InvalidEnvelope_ReportsSpecificReason(string key, string value, string reason)
    {
        var field = Field();
        var error = Assert.ThrowsExactly<InvalidDataException>(() => field.ImportEntry(Changed(field, root => root[key] = JsonNode.Parse(value))));
        Assert.AreEqual(TranslationService.Format("Plugins_Entry" + reason, JsonNode.Parse(value)!.ToString(),
            key == "pluginId" ? field.PluginId : field.SchemaField.Key), error.Message);
        Assert.IsEmpty(field.ArrayItems);
        Assert.IsFalse(field.IsDirty);
    }

    [TestMethod]
    [DataRow("{")]
    [DataRow("")]
    [DataRow("{\"format\":1,\"format\":2}")]
    public void Import_CorruptFile_ReportsInvalidContent(string json)
    {
        var error = Assert.ThrowsExactly<InvalidDataException>(() => Field().ImportEntry(Encoding.UTF8.GetBytes(json)));
        Assert.AreEqual(TranslationService.Get("Plugins_EntryInvalid"), error.Message);
    }

    [TestMethod]
    [DoNotParallelize]
    public void Import_MismatchedPluginAndVersion_MessagesIncludeSourceAndTarget()
    {
        var original = TranslationService.LookupFunc;
        TranslationService.LookupFunc = key => key + ": {0} -> {1}";
        try
        {
            var field = Field();
            var error = Assert.ThrowsExactly<InvalidDataException>(() => field.ImportEntry(Changed(field, root => root["pluginId"] = "other.plugin")));
            Assert.AreEqual($"Plugins_EntryPluginMismatch: other.plugin -> {field.PluginId}", error.Message);
            error = Assert.ThrowsExactly<InvalidDataException>(() => field.ImportEntry(Changed(field, root =>
            {
                root["pluginVersion"] = "999.0.0";
                root["fields"]!.AsObject().Remove("Title");
            })));
            Assert.AreEqual($"Plugins_EntryVersionIncompatible: 999.0.0 -> {field.PluginVersion}", error.Message);
        }
        finally { TranslationService.LookupFunc = original; }
    }

    public static IEnumerable<(Exception error, string reason)> FileErrors =>
    [
        (new FileNotFoundException("missing.json"), "FileNotFound"),
        (new DirectoryNotFoundException("missing directory"), "DirectoryNotFound"),
        (new UnauthorizedAccessException("read-only"), "AccessDenied"),
        (new IOException("locked", unchecked((int)0x80070020)), "FileInUse"),
        (new IOException("locked region", unchecked((int)0x80070021)), "FileInUse"),
        (new IOException("disk full", unchecked((int)0x80070070)), "DiskFull"),
        (new IOException("disk full", unchecked((int)0x80070027)), "DiskFull"),
        (new PathTooLongException("long path"), "InvalidPath"),
        (new ArgumentException("invalid path"), "InvalidPath"),
        (new NotSupportedException("unsupported path"), "InvalidPath"),
        (new IOException("device disconnected"), "IoError"),
        (new InvalidOperationException("no selection"), "Unavailable")
    ];

    [TestMethod]
    [DynamicData(nameof(FileErrors))]
    [DoNotParallelize]
    public void ErrorMessage_FileFailures_ReportsCategoryAndSystemDetails(Exception error, string reason)
    {
        var original = TranslationService.LookupFunc;
        TranslationService.LookupFunc = key => key + ": {0}";
        try { Assert.AreEqual($"Plugins_Entry{reason}: {error.Message}", PluginConfigEntryTransfer.ErrorMessage(error)); }
        finally { TranslationService.LookupFunc = original; }
    }

    [TestMethod]
    public void ErrorMessage_ValidationFailure_PreservesSpecificReason()
    {
        Assert.AreEqual("invalid Icon", PluginConfigEntryTransfer.ErrorMessage(new InvalidDataException("invalid Icon")));
    }

    [TestMethod]
    public void Export_InvalidUtf16AndOversizedEntry_RejectsWithoutReplacingCharacters()
    {
        var field = Field();
        field.ImportEntry(Entry(field));
        var title = field.SelectedArrayItem!.Children.Single(c => c.SchemaField.Key == "Title");
        title.Value = "broken\uD800";
        Assert.ThrowsExactly<InvalidDataException>(() => field.ExportEntry());
        title.Value = new string('a', PluginConfigEntryTransfer.MaxFileBytes);
        Assert.ThrowsExactly<InvalidDataException>(() => field.ExportEntry());
    }

    [TestMethod]
    public void Import_TextConstraintsAndSchemaDefaults_EnforcesCurrentRules()
    {
        var field = Field();
        var bytes = Changed(field, root => root["item"]!["Title"] = "long");
        var title = field.SchemaField.SubFields!.Single(f => f.Key == "Title");
        title.MaxLength = 3;
        Assert.ThrowsExactly<InvalidDataException>(() => field.ImportEntry(bytes));
        title.MaxLength = 0;
        title.RequireNonEmpty = true;
        bytes = Changed(field, root => root["item"]!.AsObject().Remove("Title"));
        Assert.ThrowsExactly<InvalidDataException>(() => field.ImportEntry(bytes));
    }

    [TestMethod]
    [DoNotParallelize]
    public void Commit_ImportedEntry_NotifiesPluginAndCanBeReadAfterSavingJson()
    {
        var field = Field();
        var notifications = 0;
        void ChangedSetting(string pluginId, string key)
        {
            if (pluginId == field.PluginId && key == field.SchemaField.Key) notifications++;
        }
        var originalGetter = PluginSettingsService.GetSettingFunc;
        var directory = Directory.CreateTempSubdirectory("entry-persistence-");
        PluginSettingsService.SettingChanged += ChangedSetting;
        try
        {
            field.ImportEntry(Changed(field, root => root["item"]!["Keyword"] = "entry-test"));
            Assert.AreEqual(0, notifications);
            field.Commit();
            Assert.AreEqual(1, notifications);
            var path = Path.Combine(directory.FullName, "commands.json");
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(field.Settings.PluginSettings));
            var persisted = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(File.ReadAllBytes(path))!;
            PluginSettingsService.GetSettingFunc = (pluginId, key, fallback) =>
                persisted.TryGetValue(pluginId, out var settings) && settings.TryGetValue(key, out var value) ? value : fallback;

            var provider = new CustomCommandsInstantProvider();
            Assert.AreEqual("entry-test", Assert.ContainsSingle(provider.QueryTriggerKeywords));
            Assert.AreEqual(ComplexText, Assert.ContainsSingle(PluginSettingsService.GetSetting<List<CustomCommandsInstantProvider.CommandItem>>(
                field.PluginId, field.SchemaField.Key, [])).Title);
        }
        finally
        {
            PluginSettingsService.SettingChanged -= ChangedSetting;
            PluginSettingsService.GetSettingFunc = originalGetter;
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void SupportsEntryTransfer_RequiresOptInVersionAndFlatSupportedFields()
    {
        var field = Field();
        Assert.IsTrue(field.SupportsEntryTransfer);
        field.SchemaField.AllowEntryTransfer = false;
        Assert.IsFalse(field.SupportsEntryTransfer);
        field.SchemaField.AllowEntryTransfer = true;
        field.SchemaField.SubFields![0].FieldType = ConfigFieldType.StringList;
        Assert.IsFalse(field.SupportsEntryTransfer);
        Assert.IsFalse(new PluginConfigFieldViewModel("plugin", Field().SchemaField, new UserSettings()).SupportsEntryTransfer);
    }

    [TestMethod]
    [DataRow("CON", "entry-CON.json")]
    [DataRow("中文<>:\"/\\|?*", "中文_________.json")]
    [DataRow(".. ", "entry.json")]
    public void SuggestedFileName_SanitizesWindowsNames(string title, string expected)
    {
        var field = Field();
        field.ImportEntry(Changed(field, root => root["item"]!["Title"] = title));
        Assert.AreEqual(expected, PluginConfigEntryTransfer.SuggestedFileName(field.SelectedArrayItem!));
    }

    [TestMethod]
    public void WriteFile_OverwriteAndLockedTarget_PreservesOldFileOnFailureAndCleansTemporary()
    {
        var directory = Directory.CreateTempSubdirectory("entry-transfer-");
        var path = Path.Combine(directory.FullName, "中文.json");
        try
        {
            var bytes = Entry(Field());
            PluginConfigEntryTransfer.WriteFile(path, bytes);
            CollectionAssert.AreEqual(bytes, PluginConfigEntryTransfer.ReadFile(path));
            PluginConfigEntryTransfer.WriteFile(path, [1, 2, 3]);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.ThrowsExactly<UnauthorizedAccessException>(() => PluginConfigEntryTransfer.WriteFile(path, bytes));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
            Assert.HasCount(1, Directory.GetFiles(directory.FullName));
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
                stream.SetLength(PluginConfigEntryTransfer.MaxFileBytes + 1);
            Assert.ThrowsExactly<InvalidDataException>(() => PluginConfigEntryTransfer.ReadFile(path));
        }
        finally { directory.Delete(recursive: true); }
    }
}
