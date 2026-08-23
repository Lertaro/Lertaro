# 架構設計

![Lertaro 架構圖](/architecture.svg)

## 處理程序拆分

Lertaro 運行為三個獨立處理程序，按權限級別和生命週期有意拆分:

- **`Lertaro.Service`** —— 一個以 `LocalSystem` 身份運行的 Windows 服務。它負責全部檔案索引工作:讀取 NTFS/ReFS 磁碟的 USN 日誌與 MFT、直接遍歷並監聽其他本地檔案系統(它們沒有日誌可讀)、掃描並快取網絡共享，並通過命名管道回答搜尋查詢。在 SYSTEM 級別運行意味着它可以讀取所有使用者帳戶都被允許看到的原始卷中繼資料，而不需要讓交互式的 App 處理程序獲得它本不需要的提升權限。
- **`Lertaro.App`** —— 使用者態、Session 級別的 WPF 應用:搜尋視窗、設定視窗、熱鍵處理、動作選單/QuickLook 介面都在這裏。它通過命名管道(`Core.Services` 裏的 `SearchService`/
  `UsnServicePipeServer`)和 Service 通信，從不直接訪問磁碟索引。它自己也額外託管了一條按使用者區分的管道(`AppSearchPipeService`)，讓 `lff` 命令列伴侶工具(參見[命令列搜尋](../user-guide/cli))
  能複用 App 已經初始化好的搜尋狀態——已加載的別名/插件提供方、已配置的網絡盤索引——而不用一個獨立的用戶端處理程序自己重新初始化一遍。
- **`Lertaro.Service --hook`** —— 一個獨立的小處理程序，專門託管低層級全局鍵盤鉤子，這樣鉤子崩潰或者某個前臺應用行為異常都不會連累主 App 處理程序。它還會加載插件的視窗集成適配器，並在自己處理程序裏執行這些調用——見下文[插件在架構中的位置](#插件在架構中的位置)。

## 共享的 Core

`Core` 是一個被 Service 和 App 同時引用的類庫。它包含:

- 搜尋引擎(`Core/SearchIndex/Fzf/*`)—— 一套仿照 `fzf` 命令列工具算法實現的模糊匹配引擎，配合一個查詢解析器(`SearchQueryParser`)處理盤符定向和路徑模式搜尋。
- 運行時索引(`Core/IndexV2/*`)—— 由 USN/MFT 讀取結果構建的記憶體映射列式快照格式，配合一個記憶體中的增量覆蓋層記錄快照之後的變更。
- IPC 契約(`SearchRequestMessage`、`SearchResponseBinarySerializer` 等)—— App 和 Service 兩邊完全共用同一份定義，保證雙方對線路格式的理解始終一致。
- `Logger` —— 寫入各處理程序獨立的日誌檔案(`service.log`、`app.log`、`hook.log`)，都可以在 App 的設定 → 運行狀態 日誌查看器裏讀取(但不是都能直接寫入)。

## 插件在架構中的位置

插件是引用 `PluginSdk` 的 `.dll` 程式集，由 App 進程加載(見[快速上手](./getting-started)和[打包與發布](./packaging))。Lertaro 自帶內建插件作為一等公民示例——`Lertaro.Plugins.CoreExtensions`(內建檔案動作與 Shell 右鍵選單集成)、`Lertaro.Plugins.PinyinAlias`(中文檔案名拼音別名)以及 `Lertaro.Plugins.FlowLauncherBridge`(橋接 C#、Python 3.12、Node.js v20 LTS 及可執行檔案格式的第三方 Flow Launcher 插件並提供純淨隔離運行環境)——參見[插件示例](./examples)了解原生插件的詳細走讀。

插件從不直接和 Service 通信;它們通過插件 SDK 參考裏記錄的接口和 App 交互，如果需要索引自訂目錄，則通過 `DirectoryIndexerService` 代為向 Service 轉發請求。

視窗集成類適配器是"只在 App 裏跑"這條規則的唯一例外:
[`IActivePathCollector`、`IFileDialogAdapter`、`IInlineSearchAdapter`](./sdk/system-adapters) 的實現會被再加載一份到 Hook 處理程序裏，它們的調用實際在 Hook 處理程序裏執行，而不是在 App 裏。這也是為什麼
Lertaro 能操作一個以系統管理員身份運行的檔案總管/檔案對話方塊/第三方檔案管理器視窗——即使 App
本身從來不提權:Windows 不允許低權限處理程序向高權限處理程序發送輸入，所以這類調用必須從一個和目標視窗權限級別相同的處理程序發起。
