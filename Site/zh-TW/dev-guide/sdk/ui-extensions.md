# 介面與預覽擴充

## 結果展示

### `ISidebarFilterProvider`

給結果側欄新增分類篩選分組(例如日期區間或檔案大小級距)。

```csharp
interface ISidebarFilterProvider
{
    int SortOrder { get; } // 預設 100;數值越小越靠前繪製
    IEnumerable<SidebarFilterGroup> GetFilterGroups();
}
```

`SidebarFilterGroup` 有一個 `Header`、一個 `AllowMultiSelect` 開關(預設 `false`;開啟後這個分組允許同時選取多項,用 OR 組合——如果分組裡的選項只在單選時才有意義(比如互相重疊/累進的日期區間),就不要開啟它),以及一份 `SidebarFilterItem` 清單(Id、DisplayName、可選圖示，以及一個可選的、對目前結果清單做非同步篩選的 `FilterPredicate`)。宿主會在分組有選取項目時自動顯示一個清空按鈕, 所以 provider 不需要自己維護一個「全部」/「任意」偽選項。

### `IResultColumnProvider`

給結果表格檢視注入額外的欄(檔案大小、修改日期、自訂中繼資料等等)。

```csharp
interface IResultColumnProvider
{
    IEnumerable<ResultColumnDefinition> GetColumns();
    string GetCellValue(ISearchResult result, string columnId);
}
```

`ResultColumnDefinition` 攜帶欄位 id、表頭文字、寬度，以及可選的 `VisibilityPredicate`/
`SortComparer` 委派。

## 快速面板

### `IQuickPanelTabProvider`

給[快速面板](../../user-guide/settings/quick-panel)貢獻一整個標籤——那個停靠在前景視窗上的浮動面板。標籤以元件命名，裡面裝一份清單，項目由宿主用它自己的結果列渲染，所以圖示、開啟、縮圖和動作選單都是白送的。CoreExtensions 自帶五個：我的最愛、歷史記錄、Windows 歷史記錄、上次目錄和最近檔案。

```csharp
interface IQuickPanelTabProvider : IPluginComponent
{
    Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
```

是一個標籤，而不是塞進別人標籤裡的一個分組：提供器給出的是一整份清單，它和某個工作區收集的資料夾是正交的，所以它跟那些資料夾並排放，而不必被逐個勾進每一個工作區。

`GetEntriesAsync()` 在面板每次被呼出時呼叫，並且回傳的是一份完整結果而不是串流的：面板要把項目當作一個**整體**來排序和截斷(最新在前，且最多幾筆)，所以它沒辦法只顯示其中一半而不在每次新項目到達時重排。這並不會帶來延遲——每個標籤都在各自的工作上載入，面板在第一個到達時就開啟，所以一個需要慢慢去找的提供器只會拖慢自己那個標籤。但仍然請遵守權杖：面板關閉時它會被取消。

來源知道修改時間的話，就填進 `ISearchResult.Metadata` 的 `Modified`——預設的「最新在前」會用它，沒有修改時間的項目則維持你回傳時的順序。什麼都沒回傳的提供器不會有標籤；擲出例外的提供器只賠上自己這一個標籤，不影響其他。

標籤預設以縮圖平鋪開啟，除非使用者在設定 → 快速面板 → 外掛標籤裡為它勾上**以清單顯示**；面板自己標題列上的檢視開關在面板開啟期間仍然可以覆蓋它。用 **×** 關閉一個標籤和在設定 → 外掛裡停用該元件是刻意區分開的兩件事：前者只是把它移出標籤列(在同一個頁面上勾回來即可)，後者則讓它壓根不再載入。宿主用元件 id 作為穩定 Key 來記住關閉狀態和顯示方式，所以外掛被關掉期間關閉的標籤，外掛回來時依然是關著的。

## 預覽與縮圖

### `IFilePreviewProvider`

在 QuickLook 預覽面板裡繪製自訂的 WPF `UIElement`(見[動作選單與預覽 → QuickLook 預覽](../../user-guide/actions-and-preview#quicklook-預覽))，用於你想特殊處理的檔案類型。

```csharp
interface IFilePreviewProvider
{
    string Name { get; }
    int Priority { get; } // 預設 0;數值越大越先執行
    bool CanPreview(string path, bool isDir);
    UIElement CreatePreview(string path, bool isDir);
    bool RendersExternally { get; } // 預設 false
}
```

`Priority`只是*預設*的順序——使用者可以在 設定 → 通用 →
[預覽與縮圖](../../user-guide/settings/general#預覽與縮圖)裡自由調整各個提供者的順序(包括相對於你的這個 provider)，這個使用者設定會覆蓋 `Priority` 回傳的值。不要假設你的 provider 宣告的優先權就是它實際執行的順序。

兩個可選的配套介面可以進一步最佳化預覽行為:

- **`IPreviewSessionAware`** —— 如果預覽提供者自身持有開銷較大的處理程序外資源(受控的原生處理程序、檔案鎖定)，就在預覽提供者本身上實作這個介面;`EndPreviewSession()` 只在整個預覽工作階段結束時呼叫一次，而不是每次切換預覽目標都呼叫。唯一的例外:如果這個 provider 的 `RendersExternally`
  為 true，宿主會在每次從它切換走的時候都呼叫一次，不只是工作階段真正結束的時候——見下文。
- **`IReusablePreview`** —— 如果 `CreatePreview` 回傳的 `UIElement` 能夠重新指向一個新檔案，而不需要從頭重建，就在它上面實作這個介面:`TrySetTarget(path, isDir)` 回傳 `true` 表示已經原地處理好了變更，回傳 `false` 則告訴宿主需要重新建構一個新的預覽。

`RendersExternally` 適用於真正的預覽內容繪製在一個獨立的、由外部管理的視窗裡、而不是
`CreatePreview` 回傳的那個 `UIElement` 上的情境——比如把檔案整個交給另一個應用程式去處理。當勝出的 provider 設定了這個屬性，宿主會隱藏自己的預覽面板，而不是顯示 `CreatePreview` 的內容(反正也不會真的顯示出來，所以可以隨便回傳一個佔位用的空內容)。配合 **`IReceivesPreviewPanelBounds`**
使用，可以拿到宿主自己那個預覽面板本該佔據的螢幕矩形(實體像素)，這樣外部視窗就能被擺到那個位置，而不是隨便出現在別的地方:

```csharp
interface IReceivesPreviewPanelBounds
{
    void OnPreviewPanelBoundsAvailable(int left, int top, int width, int height);
}
```

內建的(實驗性)QuickLook 橋接外掛就是一個真實範例:它透過具名管道探測一個外部的
[QuickLook](https://github.com/QL-Win/QuickLook) 應用程式，如果能連上，就把它的視窗停靠到宿主面板原本的位置，覆蓋所有檔案/資料夾——具體的使用者可見行為見[動作選單與預覽 → 透過 QuickLook 的外部預覽](../../user-guide/actions-and-preview#透過-quicklook-的外部預覽-可選)。注意這和 Lertaro
自己內建的預覽面板是兩回事——本程式碼庫和文件裡也習慣把那個內建面板非正式地稱為「QuickLook」。

### `IThumbnailProvider`

覆蓋符合結果顯示的圖示/縮圖。

```csharp
interface IThumbnailProvider : IPluginComponent
{
    int Priority { get; } // 預設 0;數值越大越先執行
    bool CanProvideThumbnail(string path, bool isDir);
    ImageSource? GetThumbnail(string path, int size);
}
```

跟上面 `IFilePreviewProvider.Priority` 的說明一樣:這只是預設順序,使用者可以在 設定 → 通用 →
[預覽與縮圖](../../user-guide/settings/general#預覽與縮圖)裡覆蓋它(這兩種 provider 的排序清單在同一個標籤頁裡)。

## 佈景主題與當地化

### `IThemeProvider` / `ITheme`

註冊一個或多個自訂 WPF 資源字典，作為可選佈景主題(顯示在**設定 → 通用 → 介面佈景主題**裡)。

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

為指定文化提供介面字串——可以是外掛自己的介面文字，也可以像 `PinyinAlias` 那樣，僅僅是它自己的顯示名稱。參見[外掛範例](../examples)了解一個把這個介面和另一個不相關介面實作在同一個類別上的外掛。

```csharp
interface ITranslationProvider
{
    string Name { get; }
    IReadOnlyList<string> SupportedCultures { get; } // 例如 "zh-CN"、"en-US"
    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```

`TranslationService.LoadEmbeddedTranslations`(見[宿主服務](./services))是用內嵌在外掛 DLL 裡的 JSON 檔案支撐這個介面的標準做法。
