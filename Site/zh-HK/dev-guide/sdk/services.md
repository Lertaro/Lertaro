# 宿主服務

`PluginSdk.Services` 裏的靜態服務，把宿主應用的功能暴露給插件——每一個都是包裝了宿主啓動時接好的委託的薄靜態類，所以不管底層實際運行的是什麼，插件的調用方式都一樣。

| 服務 | 用途 |
|---|---|
| `FuzzyMatchService` | `IsMatch(pattern, text)` —— `text`(或它的某個別名)是否匹配 fzf 語法的 `pattern`,用的是和宿主自身搜尋完全一致的匹配邏輯;`GetHighlightMask(text, query)` —— 對應這一對 (text, query) 的逐字元高亮掩碼,用的是宿主自己那套字面量/模糊/別名多級兜底算法(含中文拼音),這樣插件自己結果的高亮就能和其他結果保持一致,而不是只能處理簡單的字面量子串匹配。 |
| `TranslationService` | `Get(key)` / `Format(key, args)` 在運行時按當前語言查詢;`LoadEmbeddedTranslations(assembly, cultureKey, typeName)` 加載插件自己內嵌的 JSON 語言包;`GetSupportedCultures(assembly)`;`GetCurrentCulture()` —— 應用當前選定的介面語言(比如 `"zh-CN"`),這是一個獨立於操作系統語言的使用者設定。只有你確實需要拿到原始語言代碼本身時才用它(比如塞進 HTTP 的 `Accept-Language` 請求頭,或者決定翻譯 API 的目標語言)——`CultureInfo.CurrentUICulture` 反映的是操作系統的語言,不是這個設定,一旦使用者的 Windows 語言和應用內語言不一致,兩者就會悄悄對不上。 |
| `IconService` | `GetIcon(path, isDir)` 和 `GetThumbnail(path, size)` —— 帶快取的 Shell 圖示/縮略圖提取，插件不需要自己調 Windows 圖示 API。 |
| `FavoritesService` | `GetFavorites()` —— 只讀訪問使用者的[收藏夾](../../user-guide/settings/favorites)列表(`FavoriteItem`:Name、Path)。 |
| `HistoryService` | `GetHistoryEntries()` —— 每一條已記錄的[歷史記錄](../../user-guide/settings/history)條目,按最近打開優先排序,類型是 `HistoryEntry { Keyword, Path, Kind, Time }`(`Kind` 是 `HistoryEntryKind`:`File` / `Folder` / `Application`;`Keyword` 是打開時輸入框裏的搜尋文字,沒打字就直接打開的話(比如從快速面板的標籤裏點開)就是空字串;`Time` 是 Unix 秒)。同一個路徑最多只會出現一次,歸屬於最近一次帶它進來的那個關鍵字。 |
| `FileMetadataService` | `GetMetadataAsync(paths)` —— 批量查詢 Size/Created/Modified/Accessed([`FileMetadata`](./abstractions#filemetadata))，用於查詢**不屬於**你當前結果集的路徑——每個 `ISearchResult` 本身就通過自己的 `Metadata` 屬性免費攜帶這些資料(參見[共享抽象契約](./abstractions#isearchresult))，所以只有拿到的路徑不是來自結果對象(比如來自你自己的配置)時才需要用這個服務。 |
| `DirectoryIndexerService` | `RegisterDirectory(pluginId, path, recursive, filterPattern)` / `UnregisterDirectories(pluginId)` / `SearchDirectoriesAsync(pluginId, query, token)` —— 讓插件註冊自己的目錄進行後臺索引和 USN 監聽，而不用自己重新實現這套機制。`WatchDirectories(pluginId, onChanged)` 會在**你自己註冊的**目錄發生磁碟變化時回調你，返回一個 `IDisposable` 用於退訂。它是按註冊方投遞而不是廣播：沒有 id 要比對，也不會誤處理別人的變化——變化落在誰的註冊上，宿主本來就知道。回調在後台執行緒觸發，而且已經做過防抖(一次批量複製在目錄安靜下來後只回調一次，而不是每個檔案一次)；只有宿主能確實歸因到你的目錄的變化才會通知，歸因不了時(比如整棵樹被重新掃描替換)寧可通知你也不會保持沉默。`EnumerateDirectoryAsync(path, recursive, filterPattern, limit, token)` 從同一份索引(而不是檔案系統)列出某個目錄的內容——宿主已索引的碟完全不產生磁碟 I/O，沒索引的目錄則自動改為實時遍歷，調用方不需要自己判斷屬於哪種情況。它是串流式的；`filterPattern` 篩的是**檔案**(目錄一律返回，不需要就按 `IsDir` 過濾)；隱藏和系統條目永遠不返回；遞歸列舉時值得設 `limit`——`EnumerateDirectoryAsync(@"C:\", recursive: true)` 會老老實實把整個卷的每一條都交給你。 |
| `RecentFilesService` | `GetRecentFilesAsync(directories, limit, maxAgeMinutes, token)` —— 一組目錄下最新的項目，最近的在前，由宿主的記憶體索引回答而不是去讀磁碟。是把這些目錄當作**一份**合併列表，而不是每個目錄一份。只含檔案：資料夾自己的修改時間在裏面增刪任何東西時都會變，那會把「正在其中工作」的資料夾頂到一份本該顯示「工作了什麼」的列表最前面。`limit` 傳 0 表示不限條數，`maxAgeMinutes` 傳 0 表示不限時間，但兩個都不設的話，一個閒置的資料夾會僅僅因為沒有更新的東西就一直端出一個月前的檔案。宿主沒有索引的目錄不會被即時走訪，而是乾脆不貢獻任何項目——這裏要的是快答案，要麼沒有；慢的那條路請用 `DirectoryIndexerService.EnumerateDirectoryAsync`。 |
| `ExplorerPathService` | `GetLastActivePath()` —— 檔案總管視窗或檔案對話方塊最後顯示的那個資料夾，從來沒有過則為 `null`。它由宿主自己的視窗追蹤填入，跟的是**所有**應用程式的檔案對話方塊，而不只是 Lertaro 自己的介面，所以它的含義是「使用者最後真正在看的那個資料夾」——這是插件自己算不出來的。方向和 [`IActivePathCollector`](./core-search-actions) 相反：那個是插件**告訴**宿主某個第三方檔案管理員正在顯示什麼，這個是向宿主打聽。不保證仍然存在：它記的是使用者去過哪，而那個資料夾可能早已被刪掉或拔掉了。 |
| `PluginSettingsService` | `GetSetting<T>(pluginId, key, defaultValue)` —— 從宿主的配置存儲裏只讀訪問插件自己持久化的設定。回退分三層:使用者存過就用持久化的值;沒存過就用你 `IConfigurable` schema 裏該欄位自己聲明的 `DefaultValue`;兩者都沒有才輪到你傳進來的 `defaultValue` 兜底——這樣 schema 裏聲明的預設值就是唯一權威來源,調用方不需要在代碼裏再手寫一份重複的預設值。如果你把某個設定快取了起來而不是每次都重新讀取,記得訂閱 `SettingChanged(pluginId, key)` 事件,在它為你的插件觸發時清空快取——宿主是在設定頁保存之後立刻觸發這個事件的,這是唯一可靠的失效時機(不管是按鍵觸發還是輪詢檢查,都要等到別的什麼東西湊巧觸發了才會看到變化,或者乾脆永遠看不到)。 |
| `SearchRefreshService` | `RefreshIfMatches(queryMatches)` —— 給資料是異步到達的 `IInstantResultProvider` 用的(參見 [`IInstantResultProvider`](./core-search-actions#iinstantresultprovider)):等你的後臺請求完成、結果也快取好之後，調用這個方法並傳入一個基於當前查詢文字的判斷函數，宿主會把所有匹配這個判斷的、正在進行的搜尋重新跑一遍，這樣剛快取好的結果就能直接顯示出來，不需要使用者重新輸入。 |
| `Logger` | `Log(message, level = LogLevel.Info)` —— 寫入 App 的日誌檔案，和宿主自己的日誌行一樣，顯示在**設定 → 運行狀態 → App** 裏。 |
| `PluginPromptService` | `Prompt(title, fields, initialValues?)` —— 彈出一個小的模態視窗，向使用者詢問給定[`PluginConfigField`](./abstractions#iconfigurable)欄位的值(用的正是 `IConfigurable` 的配置對話方塊那套欄位 schema/渲染邏輯)，按 `Key` 匹配從 `initialValues` 預填，沒有就用各欄位自己的 `DefaultValue`。返回按欄位 `Key` 索引的填寫結果，使用者取消則返回 `null`——這些值不會讀取或寫入插件真正持久化的設定，所以可以放心複用某個配置欄位的 schema 單純做一次性輸入(比如"添加前先給它起個名字")，不會碰到背後真實的那個設定項。 |
| `UserDataService` | `GetUserDataDirectory()` —— 返回當前用戶的專屬數據目錄（如安裝版的 `%LOCALAPPDATA%\Lertaro` 或便攜版的 `Data\Users\<hash>`），用於存放用戶私有配置與插件數據；`GetSharedDataDirectory()` —— 返回機器級全局共享數據目錄（如安裝版的 `%PROGRAMDATA%\Lertaro` 或便攜版的 `Data\Machine`），用於跨多用戶共享運行時（如 Python / Node.js 嵌入環境）與全局公共快取。 |

`LogLevel` 是 `Error` / `Warn` / `Info` / `Debug`，與[運行狀態日誌查看器](../../user-guide/settings/service-status)裏的等級過濾器一致。

## Shell 檔案操作

`Lertaro.PluginSdk.Shell.FileOperations` —— 對 Windows shell 自己的 `IFileOperation` 的一層薄封裝。插件搬動檔案時，用戶看到的是和檔案總管一模一樣的進度對話框、「檔案已存在」提示和復原記錄，而不是一次行為略有不同的 `System.IO` 調用。

| 幫助類 | 用途 |
|---|---|
| `ShellPasteHelper` | `PasteAsync(sourcePaths, destinationFolder, move, onCompleted?)` —— 把任意多個路徑複製(或移動)進同一個資料夾，合併成**一次** shell 操作，所以跨碟的多選也只彈一個對話框，而不是每個檔案一個。發出即返回：native 對話框可能被用戶晾在那裏，阻塞調用方只會把介面凍住。`onCompleted` 在操作結束時觸發——不管是複製完了還是用戶取消了，因為對一個正顯示目標資料夾的視圖來說，這兩種情況的應對是同一個：回去重新看一眼。 |
| `ShellDeleteHelper` | `DeleteAsync(paths, permanent)` —— 放進回收筒或永久刪除，同樣合併成一次操作、一次確認。同樣是發出即返回。 |
| `VirtualFileExtractor` | `HasVirtualFiles(dataObject)` / `Extract(dataObject, targetFolder)` —— 把拖動攜帶的、磁碟上還不存在的檔案寫出來：從瀏覽器拖出的圖片、從郵件客戶端拖出的附件、從壓縮檔預覽裏拖出的檔案。它們都不是路徑，所以 `IDataObject.GetData(DataFormats.FileDrop)` 甚麼也拿不到；真正到來的是一份列出檔名的描述符，加上按索引一次給一個的位元組流，這個類做的就是把它拆出來。刻意不按類型過濾：拒絕拖動方願意交出來的東西，意味著要麼信副檔名、要麼嗅探位元組，這兩件事都不該由它來做。`ResolveDestination(folder, name)` 是它「重名就加 (2) 而不是覆蓋」的那套命名規則，單獨暴露出來給自己寫檔案的調用方用。 |

兩個非同步幫助類都跑在 SDK 自己的 STA 工作執行緒上(`ShellOperationStaWorker`，由宿主啟動)——shell 的 COM 介面要求 STA，共用一條意味著插件不必自己開一個套間。
