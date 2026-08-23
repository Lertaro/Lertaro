# 架構設計

![Lertaro 架構圖](/architecture.svg)

## 處理程序拆分

Lertaro 執行為三個獨立處理程序，按權限層級和生命週期有意拆分:

- **`Lertaro.Service`** —— 一個以 `LocalSystem` 身分執行的 Windows 服務。它負責全部檔案索引工作:讀取 NTFS/ReFS 磁碟的 USN 記錄檔與 MFT、直接走訪並監看其他本機檔案系統(它們沒有記錄檔可讀)、掃描並快取網路共用，並透過具名管道回答搜尋查詢。在 SYSTEM 層級執行意味著它可以讀取所有使用者帳戶都被允許看到的原始磁碟區中繼資料，而不需要讓互動式的 App 處理程序取得它本不需要的提升權限。
- **`Lertaro.App`** —— 使用者態、Session 層級的 WPF 應用程式:搜尋視窗、設定視窗、熱鍵處理、動作選單/QuickLook 介面都在這裡。它透過具名管道(`Core.Services` 裡的 `SearchService`/
  `UsnServicePipeServer`)和 Service 通訊，從不直接存取磁碟索引。它自己也額外代管了一條按使用者區分的管道(`AppSearchPipeService`)，讓 `lff` 命令列夥伴工具(參見[命令列搜尋](../user-guide/cli))
  能重複使用 App 已經初始化好的搜尋狀態——已載入的別名/外掛提供方、已設定的網路磁碟索引——而不用一個獨立的用戶端處理程序自己重新初始化一遍。
- **`Lertaro.Service --hook`** —— 一個獨立的小型處理程序，專門代管低層級全域鍵盤鉤子，這樣鉤子當機或者某個前景應用程式行為異常都不會連累主 App 處理程序。它還會載入外掛的視窗整合轉接器，並在自己的處理程序裡執行這些呼叫——見下文[外掛在架構中的位置](#外掛在架構中的位置)。

## 共用的 Core

`Core` 是一個被 Service 和 App 同時參照的類別庫。它包含:

- 搜尋引擎(`Core/SearchIndex/Fzf/*`)—— 一套仿照 `fzf` 命令列工具演算法實作的模糊比對引擎，配合一個查詢剖析器(`SearchQueryParser`)處理磁碟機代號定向和路徑模式搜尋。
- 執行階段索引(`Core/IndexV2/*`)—— 由 USN/MFT 讀取結果建構的記憶體對映欄式快照格式，配合一個記憶體中的增量覆蓋層記錄快照之後的變更。
- IPC 契約(`SearchRequestMessage`、`SearchResponseBinarySerializer` 等)—— App 和 Service 兩邊完全共用同一份定義，確保雙方對線路格式的理解始終一致。
- `Logger` —— 寫入各處理程序獨立的記錄檔(`service.log`、`app.log`、`hook.log`)，都可以在 App 的設定 → 執行狀態 記錄檢視器裡讀取(但不是都能直接寫入)。

## 外掛在架構中的位置

外掛是參考 `PluginSdk` 的 `.dll` 組件，由 App 處理程序載入(見[快速上手](./getting-started)和[封裝與發佈](./packaging))。Lertaro 自帶內建外掛作為一等公民範例——`Lertaro.Plugins.CoreExtensions`(內建檔案動作與 Shell 快顯功能表整合)、`Lertaro.Plugins.PinyinAlias`(中文檔案名稱拼音別名)以及 `Lertaro.Plugins.FlowLauncherBridge`(橋接 C#、Python 3.12、Node.js v20 LTS 及可執行檔格式的第三方 Flow Launcher 外掛並提供純淨隔離執行環境)——請參閱[外掛範例](./examples)瞭解原生外掛的詳細逐步說明。

外掛從不直接和 Service 通訊;它們透過外掛 SDK 參考裡記錄的介面和 App 互動，如果需要索引自訂目錄，則透過 `DirectoryIndexerService` 代為向 Service 轉發請求。

視窗整合類轉接器是「只在 App 裡跑」這條規則的唯一例外:
[`IActivePathCollector`、`IFileDialogAdapter`、`IInlineSearchAdapter`](./sdk/system-adapters) 的實作會被再載入一份到 Hook 處理程序裡，它們的呼叫實際在 Hook 處理程序裡執行，而不是在 App 裡。這也是為什麼 Lertaro 能操作一個以系統管理員身分執行的檔案總管/檔案對話方塊/第三方檔案管理員視窗——即使
App 本身從來不提權:Windows 不允許低權限處理程序向高權限處理程序傳送輸入，所以這類呼叫必須從一個和目標視窗權限層級相同的處理程序發起。
