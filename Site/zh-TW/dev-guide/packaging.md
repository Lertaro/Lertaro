# 封裝與發布

## 外掛是如何被發現的

App 啟動時會掃描自己的 `Plugins/` 資料夾(與 `Lertaro.App.exe` 同層)裡的每一個 `.dll`，尋找實作了 `IPlugin` 的型別。沒有單獨的清單檔——組件本身，加上它的型別實作了哪些 SDK 介面，就是完整的契約。

## 攜帶外掛自己的相依套件

如果你的外掛需要自己的受控或原生相依 DLL(比如資料庫驅動程式、原生互通程式庫……)，把它們放進 App
的 `Plugins/` 資料夾下你外掛自己的一個子目錄裡——比如 `Plugins/YourPlugin/YourPlugin.dll` 連同它的相依套件都放在同一層——而不是平舖在 `Plugins/` 根目錄下。載入器會遞迴掃描 `Plugins/`，之後
`Assembly.LoadFrom` 自帶的同目錄相依探測就會自動解析你的相依套件，不會把它們混進其他外掛的載入目錄裡。

掃描過程中遇到的非 .NET 檔案(比如原生 DLL `e_sqlite3.dll`)是預期之內的，會以 `Debug` 層級記錄，而不是 `Error`——只有真正載入失敗的受控組件才會記 `Error`。

完整的真實範例可以看 `BrowserData` 外掛的 `.csproj`:它就是這樣封裝 `Microsoft.Data.Sqlite` 及其原生相依套件 `SQLitePCLRaw`/`e_sqlite3.dll` 的，還帶了 PostBuild/PostPublish 目標，不管是哪種建置方式產生的，都會把它們歸攏進自己的子資料夾。

## 開發時自動化複製

Lertaro 自帶的外掛(`CoreExtensions`、`PinyinAlias`)都在各自的 `.csproj` 裡用一個 PostBuild 目標自動化了部署步驟，把剛編譯好的 DLL 直接複製到 App 自己輸出目錄下的 `Plugins/` 資料夾，這樣重新編譯後下次啟動就能立刻生效:

```xml
<Target Name="PostBuild" AfterTargets="PostBuildEvent">
  <Copy SourceFiles="$(TargetDir)$(TargetName).dll"
        DestinationFolder="..\..\App\bin\$(Configuration)\net10.0-windows\Plugins\"
        SkipUnchangedFiles="true" />
</Target>
```

把目標路徑改成你自己的建置輸出和 Lertaro App 安裝位置實際所在的路徑即可。

## 內嵌語言包

如果外掛實作了 `ITranslationProvider`(見[介面與預覽擴充](./sdk/ui-extensions))，把語言包 JSON
檔案作為內嵌資源封裝，而不是散落的獨立檔案，這樣它們才會跟著 DLL 一起發布:

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Translations\**\*.json" />
</ItemGroup>
```

`TranslationService.LoadEmbeddedTranslations`(見[宿主服務](./sdk/services))會在執行階段按文化名稱從組件裡把它們讀出來。

## 版本號

給外掛的 `.csproj` 加上 `<Version>`；它會顯示在**設定 → 外掛**裡對應外掛的卡片上，旁邊還會顯示你的外掛是針對哪個 `PluginSdk` 版本編譯的——在 SDK 介面發生變化時，這對確認相容性很有用。
