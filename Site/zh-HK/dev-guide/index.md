# 開發者手冊

歡迎查閱 Lertaro 開發者參考手冊。Lertaro 採用先進的解耦架構與開放的外掛模組化生態體系，提供了官方 SDK 組件 `Lertaro.PluginSdk`。開發者可以透過引用該 SDK，為 Lertaro 貢獻自訂搜尋來源、擴充快顯上下文動作、深度適配第三方檔案管理器與原生對話方塊，或者自訂美化主題與檔案預覽元件。

## 1. 架構與開發流程

- **[系統架構設計](./architecture)** —— 詳解 SYSTEM 級 Windows 索引服務、用戶態 WPF 互動程序與獨立鍵盤掛鉤程序的三程序隔離模型與具名管道 IPC 通訊機制。
- **[快速上手指南](./getting-started)** —— 從零建立外掛模組類別庫專案、引用 SDK、實作 `IPlugin` 入口以及本機偵錯的最佳實踐。
- **[打包與分發](./packaging)** —— 外掛模組組件目錄結構規範、第三方託管/原生相依庫打包、多語言 JSON 資源內嵌與 PostBuild 自動部署。
- **[官方外掛模組範例](./examples)** —— 深度剖析隨包開源的 `CoreExtensions`、`PinyinAlias` 與 `FlowLauncherBridge` 等真實外掛模組的最佳實踐程式碼。

## 2. 外掛模組 SDK 介面參考

| SDK 模組分類 | 核心介面與服務 | 關鍵功能說明 |
| :--- | :--- | :--- |
| **[核心檢索與動作](./sdk/core-search-actions)** | `ISearchableItemProvider`<br>`IInstantResultProvider`<br>`IAliasProvider`<br>`IQueryTokenProvider`<br>`ISearchResultAction`<br>`IDynamicActionProvider` | 貢獻靜態索引來源、高頻即時計算答案、非 ASCII 別名轉寫引擎、尾部 Token 後綴處理器以及靜態/動態快顯動作選單。 |
| **[系統與對話方塊適配](./sdk/system-adapters)** | `IActivePathCollector`<br>`IFileDialogAdapter`<br>`IInlineSearchAdapter`<br>`IQuickNavigationProvider` | 探測前景管理器活動目錄、掛載原生檔案對話方塊、內嵌搜尋列並雙向同步選取狀態、貢獻滑鼠快速導覽級聯選單。 |
| **[介面與預覽擴充](./sdk/ui-extensions)** | `ISidebarFilterProvider`<br>`IResultColumnProvider`<br>`IQuickPanelTabProvider`<br>`IFilePreviewProvider`<br>`IThumbnailProvider`<br>`IThemeProvider`<br>`ITranslationProvider` | 擴充側邊欄篩選分類、表格檢視自訂欄、快速面板動態工作區標籤、QuickLook 自訂轉譯器與縮圖擷取、WPF 資源字典主題包與多語言 i18n。 |
| **[共用抽象契約](./sdk/abstractions)** | `ISearchResult`<br>`FileMetadata`<br>`IPluginSearchWindow`<br>`IConfigurable` | 檢索結果唯讀資料契約、奈秒級檔案時間戳記與大小中繼資料、宿主視窗安全控制控制代碼與基於架構驅動的原生設定表單。 |
| **[宿主開放服務](./sdk/services)** | `FuzzyMatchService`<br>`TranslationService`<br>`IconService`<br>`FavoritesService`<br>`HistoryService`<br>`FileMetadataService`<br>`DirectoryIndexerService`<br>`RecentFilesService`<br>`ExplorerPathService`<br>`PluginSettingsService`<br>`SearchRefreshService`<br>`UserDataService`<br>`Logger` | 宿主暴露的高效能基礎設施：fzf 模糊比對與反白遮罩、多語言剖析、帶快取圖示擷取、收藏與歷程讀取、後台目錄索引代理、使用者資料目錄隔離及 Shell 原生檔案操作。 |

> [!NOTE]
> 本手冊所有介面簽章、方法參數與行為契約均直接對照 `Lertaro.PluginSdk` 原始碼嚴格編寫並校驗。
