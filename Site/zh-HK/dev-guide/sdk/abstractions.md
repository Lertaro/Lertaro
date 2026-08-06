# 共享抽象契約

其他 SDK 文檔頁面裏用到的模型和支援性契約。

## `ISearchResult`

每一個插件接口操作的都是這份結果的只讀視圖——插件永遠拿不到可變的結果對象，只有這個:

```csharp
interface ISearchResult
{
    string Name { get; }
    string FullPath { get; }
    string ContextDirectory { get; }
    bool IsDir { get; }
    bool IsApplication { get; }
    FileMetadata Metadata { get; }
    bool[]? GetHighlightMask(string text, string query);
}
```

`Metadata` 攜帶了宿主自己的檔案索引為每個結果生成的 Size/Created/Modified/Accessed——讀取它是免費的(不涉及磁碟 I/O 或 IPC),不像 `FileMetadataService.GetMetadataAsync`(參見[宿主服務](./services))
那樣,只有在查詢**不屬於**你當前結果集的路徑時才值得調用。

## `FileMetadata`

```csharp
readonly record struct FileMetadata(long Size, DateTime Created, DateTime Modified, DateTime Accessed);
```

本地時間。`default`(每個欄位都是零/`DateTime.MinValue`)表示"不可用"——這種結果不是由檔案索引生成的(比如來自另一個插件)。用 `Metadata.Modified != default` 來區分這種"確實不知道"的情況和一個真實的、合法的零字節檔案——後者 `Size` 確實是 `0`,但時間戳仍然是真實的。

## `IPluginSearchWindow`

傳給 `ISearchResultAction.Execute` 等回調的最小視窗控制接口——刻意保持精簡;插件應該通過它來操作結果，而不是持有真實視窗的引用:

```csharp
interface IPluginSearchWindow
{
    void LocateInExplorerExternal(string path);
    void OpenFileOrFolderExternal(string path);
    void OpenFileOrFolderAsAdminExternal(string path);
    void HideWindow();
}
```

## `IConfigurable`

和 `IPlugin` 一起實現這個接口，就能在**設定 → 插件 → 配置**裏自動獲得一個配置介面——簡單場景下不需要自己寫 WPF。

```csharp
interface IConfigurable
{
    PluginConfigSchema GetConfigSchema();
}
```

`PluginConfigSchema` 是一份扁平的 `Fields: List<PluginConfigField>`。每個 `PluginConfigField`
有一個 `Key`，可選的 `GroupKey`/`LabelKey`/`DescriptionKey`(翻譯 key，如果你有自己的
`ITranslationProvider` 就通過它解析)，一個 `FieldType`，一個 `DefaultValue`，以及——取決於類型
——`Choices`、嵌套的 `SubFields`，或者 `RequireModifier`(僅 `Hotkey` 欄位,拒絕沒有修飾鍵的單個按鍵)。

給欄位(通常是觸發關鍵詞這類 `Text` 欄位)設定 `RequireNonEmpty`，保存時如果值為空/純空白就會回退到 `DefaultValue`，而不是把空值持久化下去——否則使用者把關鍵詞欄位清空後，依賴它的功能會悄無聲息地變得不可觸發，而不是回退到一個正常的預設值。

`ConfigFieldType` 涵蓋:`Boolean`、`Text`、`Integer`、`Choice`、`Array`、`Object`、`Group`、
`StringList`、`Hotkey`、`FilePath`、`FolderPath`。參見
[CoreExtensions](../examples#coreextensions-——-動作與-shell-右鍵選單) 裏一個用到嵌套分組和
`StringList` 的真實配置模式。

## 註冊表

`ActivePathCollectorRegistry`、`FileDialogAdapterRegistry`、`InlineSearchAdapterRegistry` 是宿主把所有已加載的對應[系統適配接口](./system-adapters)實現彙總到一處的方式。插件作者通常不需要直接和這些註冊表打交道——只要實現對應接口，宿主就會自動發現並註冊你的插件。
