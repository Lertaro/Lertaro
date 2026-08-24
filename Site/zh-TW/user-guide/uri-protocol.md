# URI 通訊協定（lertaro://）

Lertaro 在首次執行時會自動在 Windows 系統中註冊自訂通訊協定 **`lertaro://`**。無論是網頁超連結、桌面捷徑、自動化指令碼還是第三方軟體，均可透過該通訊協定直接喚起 Lertaro 的特定搜尋、直達設定頁面或發起跨裝置傳輸。

## 1. 通訊協定機制與執行個體路由

- **開箱即用**：無需手動設定登錄檔，Lertaro 啟動時會自動完成註冊與自癒校驗。
- **單一執行個體路由**：若 Lertaro 已經在背景執行，開啟 `lertaro://` 連結會直接喚醒目前執行中的前景執行個體，絕不會重複啟動多個處理程序；若 Lertaro 尚未啟動，系統會自動拉起主程式並立即執行連結指定的動作。

## 2. 完整 URI 路由指令表

| URI 指令格式 | 功能說明與互動效果 |
| :--- | :--- |
| `lertaro://` | 啟用並顯示快速搜尋視窗（效果等同於按兩下 `Ctrl` 全域快速鍵）。 |
| `lertaro://search/[關鍵字]` | 啟用快速搜尋視窗，並預先填入指定的 `[關鍵字]` 並立即過濾。 |
| `lertaro://fullsearch/[關鍵字]` | 開啟大尺寸完整搜尋主視窗，並預先填入指定的 `[關鍵字]`。 |
| `lertaro://settings/page/[分區]` | 開啟設定視窗，並直接切換到指定的分區索引標籤頁。 |
| `lertaro://settings/entry/[序號]` | 開啟設定視窗並精準跳轉到某項具體設定項目，同時閃爍反白該選項。 |
| `lertaro://localsend` | 開啟空白的 LocalSend 區域網路傳送視窗。 |
| `lertaro://localsend/items/[編碼後的絕對路徑...]` | 開啟 LocalSend 並切換至檔案模式，自動新增一個或多個目標檔案/目錄。 |
| `lertaro://localsend/text/[編碼後的文字]` | 開啟 LocalSend 並切換至文字模式，自動填入指定的待傳送文字。 |

### 設定分區參數 `[分區]`

設定分區參數不區分大小寫，對應設定介面的側邊欄模組：

```text
Service      - 執行狀態
Index        - 索引設定
General      - 一般設定
Appearance   - 外觀與主題
Hotkeys      - 快速鍵設定
Plugins      - 外掛模組管理
Favorites    - 我的最愛
History      - 搜尋歷程記錄
QuickPanel   - 快速面板
About        - 關於與更新
```

> [!NOTE]
> `lertaro://settings/entry/[序號]` 中的序號是由內建的[**設定搜尋**](./instant-answers#2-關鍵字觸發功能內建外掛模組)功能動態產生的。由於內部序號在版本更新或重啟後可能會重新分配，建議在外部指令碼中優先使用 `lertaro://settings/page/[分區]`。

## 3. LocalSend 路由與參數編碼規範

在使用 LocalSend 相關 URI 時，每個檔案路徑或文字必須進行標準的 URL 編碼（例如將 `:` 轉換為 `%3A`，將 `\` 轉換為 `%5C`，將空格轉換為 `%20`）：

```text
# 預填多個檔案路徑
lertaro://localsend/items/C%3A%5CUsers%5Ctestuser%5CDesktop%5Cdoc.pdf/D%3A%5CShared%5Cphotos

# 預填待傳送文字
lertaro://localsend/text/Hello%20from%20Lertaro%21
```

- **安全條件約束**：所有傳入的檔案路徑必須為本機已經真實存在的絕對路徑；帶有預填內容的連結僅會開啟 LocalSend 並進入裝置選取介面，絕不會自動向任何裝置傳送資料。

## 4. 外部聯動實戰範例

### 瀏覽器與 Markdown 連結

在個人知識庫（如 Obsidian、Notion 或 Markdown 文件）中直接插入超連結：

```markdown
點擊開啟 [Lertaro 外觀設定](lertaro://settings/page/Appearance)
點擊快速尋找 [專案財務報表](lertaro://search/財務報表%202026)
```

### Windows 捷徑與批次處理

在桌面右鍵新建捷徑，在物件位置輸入：

```cmd
lertaro://fullsearch/D:\Projects\
```

在 PowerShell 指令碼中呼叫：

```powershell
Start-Process "lertaro://settings/page/General"
```

## 5. 安全性與未知路由容錯

- **無訊息容錯**：由於任何外部網頁或指令碼均可嘗試觸發該通訊協定，Lertaro 對所有傳入的 URI 實行嚴格的白名單校驗。若連結格式錯誤、拼寫有誤或指向不存在的路由，Lertaro 會直接安全忽略，僅記錄偵錯記錄，絕不會產生意外的破壞性操作或當機。
