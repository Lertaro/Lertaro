# 共用抽象契約

其他 SDK 文件頁面裡用到的模型和支援性契約。

## `ISearchResult`

每一個外掛介面操作的都是這份結果的唯讀檢視——外掛永遠拿不到可變動的結果物件，只有這個:

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

`Metadata` 攜帶了宿主自己的檔案索引為每個結果產生的 Size/Created/Modified/Accessed——讀取它是免費的(不涉及磁碟 I/O 或 IPC),不像 `FileMetadataService.GetMetadataAsync`(參見[宿主服務](./services))
那樣,只有在查詢**不屬於**你目前結果集的路徑時才值得呼叫。

## `FileMetadata`

```csharp
readonly record struct FileMetadata(long Size, DateTime Created, DateTime Modified, DateTime Accessed);
```

本機時間。`default`(每個欄位都是零/`DateTime.MinValue`)表示「不可用」——這種結果不是由檔案索引產生的(比如來自另一個外掛)。用 `Metadata.Modified != default` 來區分這種「確實不知道」的情況和一個真實的、合法的零位元組檔案——後者 `Size` 確實是 `0`,但時間戳記仍然是真實的。

## `IPluginSearchWindow`

傳給 `ISearchResultAction.Execute` 等回呼的最小視窗控制介面——刻意保持精簡;外掛應該透過它來操作結果，而不是持有真實視窗的參照:

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

和 `IPlugin` 一起實作這個介面，就能在**設定 → 外掛 → 設定**裡自動取得一個設定介面——簡單情境下不需要自己寫 WPF。

```csharp
interface IConfigurable
{
    PluginConfigSchema GetConfigSchema();
}
```

`PluginConfigSchema` 是一份平坦的 `Fields: List<PluginConfigField>`。每個 `PluginConfigField`
有一個 `Key`，可選的 `GroupKey`/`LabelKey`/`DescriptionKey`(翻譯 key，如果你有自己的
`ITranslationProvider` 就透過它解析)，一個 `FieldType`，一個 `DefaultValue`，以及——取決於型別
——`Choices`、巢狀的 `SubFields`，或者 `RequireModifier`(僅 `Hotkey` 欄位,拒絕沒有輔助鍵的單一按鍵)。

給欄位(通常是觸發關鍵字這類 `Text` 欄位)設定 `RequireNonEmpty`，儲存時如果值為空/純空白就會回退到 `DefaultValue`，而不是把空值持久化下去——否則使用者把關鍵字欄位清空後，依賴它的功能會悄無聲息地變得無法觸發，而不是回退到一個正常的預設值。

`ConfigFieldType` 涵蓋:`Boolean`、`Text`、`Integer`、`Choice`、`Array`、`Object`、`Group`、
`StringList`、`Hotkey`、`FilePath`、`FolderPath`。參見
[CoreExtensions](../examples#coreextensions-——-動作與-shell-右鍵選單) 裡一個用到巢狀分組和
`StringList` 的真實設定結構。

## 註冊表

`ActivePathCollectorRegistry`、`FileDialogAdapterRegistry`、`InlineSearchAdapterRegistry` 是宿主把所有已載入的對應[系統轉接介面](./system-adapters)實作彙總到一處的方式。外掛作者通常不需要直接和這些註冊表打交道——只要實作對應介面，宿主就會自動發現並註冊你的外掛。
