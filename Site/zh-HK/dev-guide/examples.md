# 官方外掛模組範例

為了幫助開發者深入理解 `Lertaro.PluginSdk` 的各模組協同機制，本章節選取了 Lertaro 官方存放庫自帶的四個典型開源外掛模組進行深度案例剖析。

## 1. CoreExtensions —— 動作、Shell 選單與快速面板

`CoreExtensions` 外掛模組是 Lertaro 最核心的功能擴充包，同時實作了 `IPlugin`、`IActionProvider`、`IConfigurable` 以及多個子提供者介面。

### 核心實作要點

- **靜態結果動作（`IActionProvider.GetActions()`）**：註冊了 10 個常用的基礎檔案動作（開啟、在檔案總管中定位、複製完整路徑、複製/剪下實體檔案、開啟終端機命令列以及提權以管理員身分執行等）。
- **原生 Shell 選單整合（`IDynamicActionProvider`）**：透過 `ShellMenuActionProvider` 與 Windows Shell COM 介面互動，將完整的 Windows 快顯級聯選單（如「傳送到」、7-Zip、VS Code 開啟等）無縫轉譯至 Lertaro 的 `Ctrl+O` 動作選單中。
- **結構描述驅動的設定表單（`IConfigurable`）**：展示了如何定義包含巢狀分組（`Group`）、多行字串清單（`StringList`）與熱鍵錄製（`Hotkey`）的複雜設定表單，無需手寫任何 XAML 即可在設定中心中自動產生。
- **多樣化的快速面板標籤（`IQuickPanelTabProvider`）**：
  - `FavoritesTabProvider` / `HistoryTabProvider`：直接將記憶體中的結構化清單包裝為結果集，屬於零 I/O 極簡實作。
  - `WindowsRecentTabProvider`：在背景任務中周遊系統 `Recent` 目錄並透過 COM 剖析捷徑目標，預先截斷並填入 `Metadata.Modified` 時間戳記以實現「最新在前」。
  - `LastDirectoryTabProvider` / `RecentFilesTabProvider`：直接呼叫宿主公開的 [`ExplorerPathService`](./sdk/services) 與 `RecentFilesService` 查詢宿主已有狀態。

## 2. PinyinAlias —— 非 ASCII 別名轉寫引擎

`PinyinAlias` 外掛模組專門為中文檔案名稱提供拼音全拼與首字母縮寫檢索支援，同時實作了 `IAliasProvider` 與 `ITranslationProvider` 兩個介面。

### 核心實作要點

- **輸入/輸出字母表邊界（`InputRanges` / `OutputRanges`）**：宣告輸入來源字元範圍為 CJK 表意文字區塊，輸出字元範圍為小寫 `a`–`z`。宿主利用該邊界智慧將「大cj」等混合查詢切分為字面比對與拼音別名比對。
- **快速預檢過濾（`CanHandle(text)`）**：在產生別名前先掃描文字中是否存在中文字元，對於純英文字串直接返回 `false`，完全跳過後續開銷。
- **多音字組合與別名建置（`GetAliases(text)`）**：先建置字元級音節表，對於含多音字的檔案名稱（如「重」、「長」），自動產生各常見讀音組合並使用 `|` 管道符連接（上限 32 種組合以防爆炸），供搜尋引擎作為候選集並行比對。
- **內嵌多語言與執行緒安全快取**：透過 `ITranslationProvider` 提供外掛模組顯示名稱與描述的多語言當地語系化，並在內部使用帶 `lock` 保護的字典快取剖析後的 JSON 翻譯，避免每次查詢重複剖析。

## 3. FlowLauncherBridge —— 跨生態橋接與隔離執行階段

`FlowLauncherBridge` 外掛模組展示了如何建置一個大型複合型橋接系統，將外部開源社群生態無縫吸納進 Lertaro 體系。

### 核心實作要點

- **多語言跨程序橋接**：相容 C# (.NET)、Python 3.12、Node.js v20 LTS 及獨立 `.exe` 形式的 Flow Launcher 外掛模組。
- **純淨自包含環境**：在使用者資料目錄中自動隔離部署 Python / Node.js 執行階段，並透過具名管道與子程序進行 JSON-RPC 通訊。
- **動態設定與富文本預覽**：剖析外部外掛模組的 `SettingsTemplate.yaml`/`.json` 並動態對應為 `PluginConfigSchema`；在 QuickLook 預覽面板中利用 WebView2 轉譯外部外掛模組返回的富文本卡片（如詞典釋義、即時天氣等）。

## 4. FileUnlocker —— 解除檔案佔用動作

`FileUnlocker` 展示一個專注於單一動作的外掛模組如何透過 Windows Restart Manager API 查詢檔案佔用並請求釋放。

### 核心實作要點

- **單選限制**：僅對一個已存在的檔案提供動作，避免對資料夾或多個結果發起含義不明確的請求。
- **程序資訊展示**：顯示佔用程序名稱、PID 和可執行檔路徑，並提供重新整理操作以應對檔案狀態變化。
- **請求式釋放**：請求佔用程序釋放檔案；未偵測到程序或操作進行中時，釋放按鈕會停用。
- **宿主視窗框架**：將 WPF 視圖放入 SDK 提供的主題化 `PluginWindow` 對話方塊，外掛模組無需重複實作主題、DPI、工作列和 Alt+Tab 處理。
