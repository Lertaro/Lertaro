# 外掛範例

Lertaro 自帶兩個外掛，都是很有參考價值的真實案例——都在 Lertaro 版本庫的 `Plugins/` 目錄下。

## CoreExtensions —— 動作與 Shell 右鍵選單

`CoreExtensionsPlugin` 同時實作了三個介面:`IPlugin`、`IActionProvider`、`IConfigurable`。

- **`IActionProvider.GetActions()`** 回傳十個內建的 `ISearchResultAction`——開啟、在檔案總管中定位、複製路徑、複製/剪下檔案本身、在其所在位置開啟命令提示字元、touch/mkdir，以及開啟和命令提示字元的提權(以系統管理員身分執行)變體。
- **`IActionProvider.GetDynamicActionProviders()`** 回傳一個 `IDynamicActionProvider`——
  `ShellMenuActionProvider`——正是它讓真正的 Windows 右鍵選單(包括「傳送到」這類串接式子選單)出現在 Lertaro 自己的動作選單裡。如果你想在 Lertaro 裡呈現*任何*外部、動態建構的選單，而不是一份固定的動作清單，這是值得照抄的模式。
- **`IConfigurable.GetConfigSchema()`** 展示了帶巢狀欄位分組和 `StringList` 欄位型別的設定結構
  ——如果你的外掛在設定 → 外掛的設定對話方塊裡需要的不只是一份平坦的布林值清單，值得讀一下這部分。
- 有五個提供器實作了
  [`IQuickPanelTabProvider`](./sdk/ui-extensions#iquickpaneltabprovider)，而且它們正好涵蓋了這個介面的兩端。`FavoritesTabProvider` 和 `HistoryTabProvider` 原樣交出一份記憶體裡的清單——最精簡參考實作，因為兩者自己都沒有額外的狀態。`WindowsRecentTabProvider` 則是另一端：它在背景工作上讀目錄、透過 COM 解析 shell 捷徑，**先**截斷再做那件昂貴的事，並給每個項目填上 `Metadata.Modified`，好讓標籤的「最新在前」真的有意義。
- `LastDirectoryTabProvider` 和 `RecentFilesTabProvider` 值得讀的理由不太一樣：它們自己壓根沒有資料，而是透過
  [`ExplorerPathService`](./sdk/services) 和 `RecentFilesService` 向宿主要。只要你的外掛想展示的東西 Lertaro 本來就知道，照抄這個模式就對了。

## PinyinAlias —— 中文檔名拼音別名

`PinyinAliasProvider` 同時實作了 `IAliasProvider` 和 `ITranslationProvider`——一個外掛可以自由組合多個相關的 SDK 角色，這是個很好的參考範本:

- **`IAliasProvider.InputRanges`/`OutputRanges`** 直接重複使用 `PinyinEngine` 自己表裡的邊界來宣告這兩個字母表(`InputRanges`:CJK 區塊;`OutputRanges`:`a`-`z`),不重複寫魔數——宿主用它們支援「大cj」比對「大長今」這類混合了字面漢字和拼音的查詢。
- **`IAliasProvider.CanHandle(text)`** 會先掃描是否存在任意中文字元，再決定要不要做實際工作，所以非中文檔名會完全跳過別名產生。
- **`IAliasProvider.GetAliases(text)`** 先建構一張按字元劃分的音節表(每個漢字對映到它可能的拼音讀音)，然後產出一個全拼別名和一個首字母別名。對於含多音字(有一種以上有效讀音)的檔名，會為每種常見讀音組合都產生別名——上限 32 種組合，防止極端輸入引發組合爆炸——用 `|` 連接各個備選項，這樣搜尋引擎會把每一個都當作候選，而不是要求它們同時全部符合。
- **`ITranslationProvider`** 實作在*同一個*類別上，純粹是為了給這個外掛自己的介面文字(比如它的顯示名稱)提供翻譯，透過 `TranslationService.LoadEmbeddedTranslations` 實作——這兩個介面用途上並無關聯，只是碰巧在這個體量很小的單一檔案外掛裡放在了同一個型別上。
- 用一個 `lock` 保護的 `Dictionary<string, Dictionary<string, string>>` 快取避免了每次呼叫
  `GetTranslations` 都重新剖析內嵌的翻譯 JSON——這是任何在 `GetTranslations` 裡做了非平凡工作的外掛都該採用的標準模式。

把這兩個外掛對照著看，是理解[外掛 SDK 參考](./sdk/core-search-actions)裡各個部分如何在實務中配合起來最快的方式。
