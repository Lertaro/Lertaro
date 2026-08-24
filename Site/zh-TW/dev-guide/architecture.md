# 系統架構設計

Lertaro 採用先進的多處理程序隔離架構與模組化分層設計，確保在實現系統級毫秒檢索與全方位視窗整合的同時，兼顧最高級別的執行安全與穩定性。

![Lertaro 架構圖](/architecture-zh-CN.svg)

## 1. 三處理程序隔離模型

為了徹底規避單一元件異常導致整個系統當機，並將 Windows 特權限制在最小範圍內，Lertaro 的執行階段被明確劃分為三個獨立的處理程序：

### 1. 背景索引服務（`Lertaro.Service`）

- **執行身分**：以 Windows 系統級 `LocalSystem` 身分常駐執行的 Windows 服務。
- **職責範圍**：承擔全盤檔案索引與增量監聽的核心重任。直接讀取 NTFS / ReFS 磁碟底層的 USN 變更記錄檔與 \$MFT 主檔案表；即時監聽 FAT32 / exFAT 磁碟變更；定時抓取並快取 SMB / NAS 網路共用。
- **安全與效能考量**：執行在 SYSTEM 級別使服務無需快顯任何 UAC 提權快顯視窗即可直接讀取原始磁碟卷的中繼資料；同時透過高效能具名管道向使用者態 App 返回檢索結果，徹底避免了讓前景 UI 處理程序持有不必要的全域高權限。

### 2. 使用者互動主程式（`Lertaro.App`）

- **執行身分**：標準 Windows 使用者態、Session 隔離的 WPF 前景桌面應用程式。
- **職責範圍**：承載快速搜尋置中浮動視窗、完整主搜尋視窗、設定中心、全域快速鍵分發、動作功能表（`Ctrl+O`）以及 QuickLook 檔案即時預覽介面。
- **IPC 橋樑與 CLI 裝載**：透過雙向具名管道（`Core.Services.SearchService`）向背景 Service 發送搜尋請求與目錄管理指令；同時，App 自身還裝載了一條面向目前使用者的專屬具名管道服務（`AppSearchPipeService`），使外部伴隨工具（如 `lff` 命令列工具）能直接複用 App 已經建置好的記憶體別名表、外掛模組提供者與網路磁碟快取，無需重複初始化。

### 3. 全域鍵盤攔截與視窗適配處理程序（`Lertaro.Service --hook`）

- **執行身分**：由背景服務按需拉起的獨立特權輔助處理程序。
- **職責範圍**：裝載低階全域鍵盤攔截（Low-Level Keyboard Hook）與滑鼠全域監聽。
- **UIPI 權限突破與當機隔離**：在 Windows 安全體系中，低完整性級別的使用者態處理程序無法向以管理員身分執行的最高權限視窗發送視窗訊息或模擬輸入（UIPI 隔離）。透過在該特權 Hook 處理程序中執行視窗整合適配器（[`IActivePathCollector`、`IFileDialogAdapter`、`IInlineSearchAdapter`](./sdk/system-adapters)），Lertaro 能夠毫無阻礙地識別並嵌入由管理員身分啟動的檔案總管、Total Commander 或第三方對話方塊。同時，即使底層攔截因第三方遊戲的反作弊模組產生異常，也不會影響主 App 處理程序的正常執行。

## 2. 共用核心層（Shared Core Library）

`Lertaro.Core` 是被 Service、App 和 Hook 處理程序同時引用的基礎類別庫，主要包含以下關鍵模組：

- **自研 fzf 模糊比對引擎（`Core/SearchIndex/Fzf/*`）**：高效復刻並最佳化了知名 `fzf` 演算法的跳躍字元模糊比對、子字串分段與字元級反白計算，配合 `SearchQueryParser` 實現磁碟機定向與路徑模式切分。
- **欄式記憶體索引（`Core/IndexV2/*`）**：採用記憶體對應欄式快照（Columnar Snapshot）與記憶體增量覆蓋層（Delta Overlay），實現億級檔案項目的亞毫秒級檢索。
- **二進位 IPC 通訊契約**：定義了 `SearchRequestMessage`、`SearchResponseBinarySerializer` 等標準二進位資料通訊協定，確保多處理程序間零拷貝高效序列化。
- **統一多處理程序記錄系統（`Logger`）**：分別輸出至 `service.log`、`app.log` 與 `hook.log`，並由 App 的設定中心記錄檢視器統一代理讀取與呈現。

## 3. 外掛模組系統在架構中的定位

所有第三方及內建外掛模組均基於 `Lertaro.PluginSdk` 建置，由 `Lertaro.App` 處理程序在啟動時自動反映掃描並載入：

- **零特權直接通訊**：外掛模組通常只與 App 處理程序進行互動，不直接與底層 Service 通訊。若外掛模組需要註冊自訂實體目錄進行長效索引，可透過 SDK 提供的 `DirectoryIndexerService` 向宿主發起代理請求。
- **雙重載入機制**：常規的搜尋來源、動作與介面擴充僅在 App 處理程序中執行；而實作了系統與視窗適配介面（`IActivePathCollector` 等）的元件會被宿主額外載入一份至 Hook 處理程序中執行，以確保跨權限視窗自動化的穩定性。
