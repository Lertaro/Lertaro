# 插件示例

Lertaro 自帶兩個插件，都是很有參考價值的真實案例——都在 Lertaro 倉庫的 `Plugins/` 目錄下。

## CoreExtensions —— 動作與 Shell 右鍵選單

`CoreExtensionsPlugin` 同時實現了三個接口:`IPlugin`、`IActionProvider`、`IConfigurable`。

- **`IActionProvider.GetActions()`** 返回十個內置的 `ISearchResultAction`——打開、在檔案總管中定位、複製路徑、複製/剪下檔案本身、在其所在位置打開命令提示字元、touch/mkdir，以及打開和命令提示字元的提權(以系統管理員身份運行)變體。
- **`IActionProvider.GetDynamicActionProviders()`** 返回一個 `IDynamicActionProvider`——
  `ShellMenuActionProvider`——正是它讓真正的 Windows 右鍵選單(包括"發送到"這類級聯子選單)出現在 Lertaro 自己的動作選單裏。如果你想在 Lertaro 裏呈現*任何*外部、動態構建的選單，而不是一份固定的動作列表，這是值得照抄的模式。
- **`IConfigurable.GetConfigSchema()`** 展示了帶嵌套欄位分組和 `StringList` 欄位類型的配置模式
  ——如果你的插件在設定 → 插件的配置對話方塊裏需要的不只是一份扁平的布爾值列表，值得讀一下這部分。
- 有五個提供器實現了
  [`IQuickPanelTabProvider`](./sdk/ui-extensions#iquickpaneltabprovider)，而且它們正好覆蓋了這個接口的兩端。`FavoritesTabProvider` 和 `HistoryTabProvider` 原樣交出一份記憶體裏的列表——最簡參考實現，因為兩者自己都沒有額外的狀態。`WindowsRecentTabProvider` 則是另一端：它在背景任務上讀目錄、透過 COM 解析 shell 捷徑，**先**截斷再做那件昂貴的事，並給每個項目填上 `Metadata.Modified`，好讓標籤的「最新在前」真的有意義。
- `LastDirectoryTabProvider` 和 `RecentFilesTabProvider` 值得讀的理由不太一樣：它們自己壓根沒有資料，而是透過
  [`ExplorerPathService`](./sdk/services) 和 `RecentFilesService` 向宿主要。只要你的插件想展示的東西 Lertaro 本來就知道，照抄這個模式就對了。

## PinyinAlias —— 中文檔案名拼音別名

`PinyinAliasProvider` 同時實現了 `IAliasProvider` 和 `ITranslationProvider`——一個插件可以自由組合多個相關的 SDK 角色，這是個很好的參考模板:

- **`IAliasProvider.InputRanges`/`OutputRanges`** 直接複用 `PinyinEngine` 自己表裏的邊界來聲明這兩個字母表(`InputRanges`:CJK 區塊;`OutputRanges`:`a`-`z`),不重複寫魔數——宿主用它們支援
  "大cj"匹配"大長今"這類混合了字面漢字和拼音的查詢。
- **`IAliasProvider.CanHandle(text)`** 會先掃描是否存在任意中文字元，再決定要不要做實際工作，所以非中文檔案名會完全跳過別名生成。
- **`IAliasProvider.GetAliases(text)`** 先構建一張按字元劃分的音節表(每個漢字映射到它可能的拼音讀音)，然後產出一個全拼別名和一個首字母別名。對於含多音字(有一種以上有效讀音)的檔案名，會為每種常見讀音組合都生成別名——上限 32 種組合，防止極端輸入引發組合爆炸——用 `|` 連接各個備選項，這樣搜尋引擎會把每一個都當作候選，而不是要求它們同時全部匹配。
- **`ITranslationProvider`** 實現在*同一個*類上，純粹是為了給這個插件自己的介面文本(比如它的顯示名稱)提供翻譯，通過 `TranslationService.LoadEmbeddedTranslations` 實現——這兩個接口用途上並無關聯，只是碰巧在這個體量很小的單檔案插件裏放在了同一個類型上。
- 用一個 `lock` 保護的 `Dictionary<string, Dictionary<string, string>>` 快取避免了每次調用
  `GetTranslations` 都重新解析內嵌的翻譯 JSON——這是任何在 `GetTranslations` 裏做了非平凡工作的插件都該採用的標準模式。

把這兩個插件對照着看，是理解[插件 SDK 參考](./sdk/core-search-actions)裏各個部分如何在實踐中配合起來最快的方式。
