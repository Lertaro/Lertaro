# 開發手冊

Lertaro 提供了一套開放的插件 SDK(`Lertaro.PluginSdk`)，第三方程式集可以引用它來擴展搜尋行為、添加右鍵選單動作、與其他視窗集成，以及自訂介面。本手冊記錄了這套 SDK 的全部內容。

- **[架構設計](./architecture)** —— App、後臺 Service 與插件之間是如何配合的。
- **[快速上手](./getting-started)** —— 搭建一個插件項目並加載它。
- **插件 SDK 參考**:
  - **[核心檢索與動作](./sdk/core-search-actions)** —— 貢獻搜尋結果與結果動作。
  - **[系統與對話方塊適配](./sdk/system-adapters)** —— 與檔案總管、原生檔案對話方塊及其他前臺視窗集成。
  - **[介面與預覽擴展](./sdk/ui-extensions)** —— 側欄過濾器、結果列、檔案預覽、縮略圖、主題與語言包。
  - **[共享抽象契約](./sdk/abstractions)** —— 插件收到的只讀模型(`ISearchResult`、
    `IPluginSearchWindow`)以及配置模式(`IConfigurable`)。
  - **[宿主服務](./sdk/services)** —— 宿主暴露給插件的靜態服務(圖示、收藏夾、歷史記錄、檔案中繼資料、目錄索引、插件專屬設定、日誌)。
- **[插件示例](./examples)** —— 兩個真實隨附插件的案例分析。
- **[打包與發佈](./packaging)** —— 編譯好的插件 DLL 是如何被發現並加載的。

本手冊裏的每一個接口簽名都直接對照當前 `PluginSdk` 源碼核實過——如果發現和文檔有出入，以代碼為準。
