# 介面與預覽擴展

## 結果展示

### `ISidebarFilterProvider`

給結果側欄添加分類過濾分組(例如日期區間或檔案大小檔位)。

```csharp
interface ISidebarFilterProvider
{
    int SortOrder { get; } // 預設 100;數值越小越靠前渲染
    IEnumerable<SidebarFilterGroup> GetFilterGroups();
}
```

`SidebarFilterGroup` 有一個 `Header`、一個 `AllowMultiSelect` 開關(預設 `false`;打開後這個分組允許同時選中多項,用 OR 組合——如果分組裏的選項只在單選時才有意義(比如互相重疊/累進的日期區間),就不要打開它),以及一份 `SidebarFilterItem` 列表(Id、DisplayName、可選圖示，以及一個可選的、對當前結果列表做異步過濾的 `FilterPredicate`)。宿主會在分組有選中項時自動顯示一個清空按鈕,
所以 provider 不需要自己維護一個"全部"/"任意"偽選項。

### `IResultColumnProvider`

給結果表格視圖注入額外的列(檔案大小、修改日期、自訂中繼資料等等)。

```csharp
interface IResultColumnProvider
{
    IEnumerable<ResultColumnDefinition> GetColumns();
    string GetCellValue(ISearchResult result, string columnId);
}
```

`ResultColumnDefinition` 攜帶列 id、表頭文字、寬度，以及可選的 `VisibilityPredicate`/
`SortComparer` 委託。

## 快速面板

### `IQuickPanelTabProvider`

給[快速面板](../../user-guide/settings/quick-panel)貢獻一整個標籤——那個停靠在前景視窗上的浮動面板。標籤以組件命名，裏面裝一份清單，項目由宿主用它自己的結果行渲染，所以圖示、打開、縮圖和動作選單都是白送的。CoreExtensions 自帶五個：我的最愛、歷史記錄、Windows 歷史記錄、上次目錄和最近檔案。

```csharp
interface IQuickPanelTabProvider : IPluginComponent
{
    Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
```

是一個標籤，而不是塞進別人標籤裏的一個分組：提供器給出的是一整份清單，它和某個工作區收集的資料夾是正交的，所以它跟那些資料夾並排放，而不必被逐個勾進每一個工作區。

`GetEntriesAsync()` 在面板每次被呼出時調用，並且返回的是一份完整結果而不是流式的：面板要把項目當作一個**整體**來排序和截斷(最新在前，且最多幾條)，所以它沒辦法只顯示其中一半而不在每次新項目到達時重排。這並不會帶來延遲——每個標籤都在各自的任務上載入，面板在第一個到達時就打開，所以一個需要慢慢去找的提供器只會拖慢自己那個標籤。但仍然請遵守權杖：面板關閉時它會被取消。

來源知道修改時間的話，就填進 `ISearchResult.Metadata` 的 `Modified`——預設的「最新在前」會用它，沒有修改時間的項目則維持你返回時的順序。什麼都沒返回的提供器不會有標籤；拋出例外的提供器只賠上自己這一個標籤，不影響其他。

標籤預設以縮圖平鋪打開，除非使用者在設定 → 快速面板 → 插件標籤裏為它勾上**以清單顯示**；面板自己標題欄上的檢視開關在面板打開期間仍然可以覆蓋它。用 **×** 關閉一個標籤和在設定 → 插件裏停用該組件是刻意區分開的兩件事：前者只是把它移出標籤欄(在同一個頁面上勾回來即可)，後者則讓它壓根不再載入。宿主用組件 id 作為穩定 Key 來記住關閉狀態和顯示方式，所以插件被關掉期間關閉的標籤，插件回來時依然是關着的。

## 預覽與縮略圖

### `IFilePreviewProvider`

在 QuickLook 預覽面板裏渲染自訂的 WPF `UIElement`(見[動作選單與預覽 → QuickLook 預覽](../../user-guide/actions-and-preview#quicklook-預覽))，用於你想特殊處理的檔案類型。

```csharp
interface IFilePreviewProvider
{
    string Name { get; }
    int Priority { get; } // 預設 0;數值越大越先運行
    bool CanPreview(string path, bool isDir);
    UIElement CreatePreview(string path, bool isDir);
    bool RendersExternally { get; } // 預設 false
}
```

`Priority`只是*預設*的順序——使用者可以在 設定 → 通用 →
[預覽與縮略圖](../../user-guide/settings/general#預覽與縮略圖)裏自由調整各個提供者的順序(包括相對於你的這個 provider)，這個使用者配置會覆蓋 `Priority` 返回的值。不要假設你的 provider 聲明的優先級就是它實際運行的順序。

兩個可選的配套接口可以進一步優化預覽行為:

- **`IPreviewSessionAware`** —— 如果預覽提供者自身持有開銷較大的處理程序外資源(託管的原生處理程式、檔案鎖)，就在預覽提供者本身上實現這個接口;`EndPreviewSession()` 只在整個預覽會話結束時調用一次，而不是每次切換預覽目標都調用。唯一的例外:如果這個 provider 的 `RendersExternally`
  為 true，宿主會在每次從它切換走的時候都調用一次，不只是會話真正結束的時候——見下文。
- **`IReusablePreview`** —— 如果 `CreatePreview` 返回的 `UIElement` 能夠重新指向一個新檔案，而不需要從頭重建，就在它上面實現這個接口:`TrySetTarget(path, isDir)` 返回 `true` 表示已經原地處理好了變更，返回 `false` 則告訴宿主需要重新構建一個新的預覽。

`RendersExternally` 適用於真正的預覽內容渲染在一個獨立的、由外部管理的視窗裏、而不是
`CreatePreview` 返回的那個 `UIElement` 上的場景——比如把檔案整個交給另一個應用程式去處理。當勝出的 provider 設定了這個屬性，宿主會隱藏自己的預覽面板，而不是顯示 `CreatePreview` 的內容(反正也不會真的顯示出來，所以可以隨便返回一個佔位用的空內容)。配合 **`IReceivesPreviewPanelBounds`**
使用，可以拿到宿主自己那個預覽面板本該佔據的螢幕矩形(物理像素)，這樣外部視窗就能被擺到那個位置，而不是隨便出現在別的地方:

```csharp
interface IReceivesPreviewPanelBounds
{
    void OnPreviewPanelBoundsAvailable(int left, int top, int width, int height);
}
```

內置的(實驗性)QuickLook 橋接插件就是一個真實例子:它通過命名管道探測一個外部的
[QuickLook](https://github.com/QL-Win/QuickLook) 應用，如果能連上，就把它的視窗停靠到宿主面板原本的位置，覆蓋所有檔案/資料夾——具體的使用者可見行為見[動作選單與預覽 → 通過 QuickLook 的外部預覽](../../user-guide/actions-and-preview#通過-quicklook-的外部預覽-可選)。注意這和 Lertaro
自己內置的預覽面板是兩回事——本代碼庫和文檔裏也習慣把那個內置面板非正式地稱為"QuickLook"。

### `IThumbnailProvider`

覆蓋匹配結果顯示的圖示/縮略圖。

```csharp
interface IThumbnailProvider : IPluginComponent
{
    int Priority { get; } // 預設 0;數值越大越先運行
    bool CanProvideThumbnail(string path, bool isDir);
    ImageSource? GetThumbnail(string path, int size);
}
```

跟上面 `IFilePreviewProvider.Priority` 的說明一樣:這只是預設順序,使用者可以在 設定 → 通用 →
[預覽與縮略圖](../../user-guide/settings/general#預覽與縮略圖)裏覆蓋它(這兩種 provider 的排序列表在同一個標籤頁裏)。

## 主題與本地化

### `IThemeProvider` / `ITheme`

註冊一個或多個自訂 WPF 資源字典，作為可選主題(顯示在**設定 → 通用 → 介面主題**裏)。

```csharp
interface IThemeProvider
{
    string Name { get; }
    IEnumerable<ITheme> GetThemes();
}

interface ITheme
{
    string Id { get; }
    string DisplayName { get; }
    bool IsDark { get; }
    double WindowOpacity { get; } // 預設 1.0
    ResourceDictionary GetResources();
}
```

### `ITranslationProvider`

為給定文化提供介面字串——可以是插件自己的介面文本，也可以像 `PinyinAlias` 那樣，僅僅是它自己的顯示名稱。參見[插件示例](../examples)瞭解一個把這個接口和另一個不相關接口實現在同一個類上的插件。

```csharp
interface ITranslationProvider
{
    string Name { get; }
    IReadOnlyList<string> SupportedCultures { get; } // 例如 "zh-CN"、"en-US"
    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```

`TranslationService.LoadEmbeddedTranslations`(見[宿主服務](./services))是用內嵌在插件 DLL 裏的 JSON 檔案支撐這個接口的標準做法。
