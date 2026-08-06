# 開發手冊

Lertaro 提供了一套開放的外掛 SDK(`Lertaro.PluginSdk`)，第三方組件可以參照它來擴充搜尋行為、新增右鍵選單動作、與其他視窗整合，以及自訂介面。本手冊記錄了這套 SDK 的全部內容。

- **[架構設計](./architecture)** —— App、背景 Service 與外掛之間是如何配合的。
- **[快速上手](./getting-started)** —— 搭建一個外掛專案並載入它。
- **外掛 SDK 參考**:
  - **[核心搜尋與動作](./sdk/core-search-actions)** —— 貢獻搜尋結果與結果動作。
  - **[系統與對話方塊轉接](./sdk/system-adapters)** —— 與檔案總管、原生檔案對話方塊及其他前景視窗整合。
  - **[介面與預覽擴充](./sdk/ui-extensions)** —— 側欄篩選器、結果欄、檔案預覽、縮圖、佈景主題與語言包。
  - **[共用抽象契約](./sdk/abstractions)** —— 外掛收到的唯讀模型(`ISearchResult`、
    `IPluginSearchWindow`)以及設定結構(`IConfigurable`)。
  - **[宿主服務](./sdk/services)** —— 宿主公開給外掛的靜態服務(圖示、我的最愛、歷史記錄、檔案中繼資料、目錄索引、外掛專屬設定、記錄)。
- **[外掛範例](./examples)** —— 兩個真實隨附外掛的案例分析。
- **[封裝與發布](./packaging)** —— 編譯好的外掛 DLL 是如何被發現並載入的。

本手冊裡的每一個介面簽章都直接對照目前 `PluginSdk` 原始碼核實過——如果發現和文件有出入，以程式碼為準。
