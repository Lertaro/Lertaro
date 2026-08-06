# 打包與發佈

## 插件是如何被發現的

App 啓動時會掃描自己的 `Plugins/` 資料夾(與 `Lertaro.App.exe` 同級)裏的每一個 `.dll`，查找實現了 `IPlugin` 的類型。沒有單獨的清單檔案——程式集本身，加上它的類型實現了哪些 SDK 接口，就是完整的契約。

## 攜帶插件自己的依賴庫

如果你的插件需要自己的託管或原生依賴 DLL(比如資料庫驅動、原生互操作庫……)，把它們放進 App 的
`Plugins/` 資料夾下你插件自己的一個子目錄裏——比如 `Plugins/YourPlugin/YourPlugin.dll` 連同它的依賴都放在同一層——而不是平鋪在 `Plugins/` 根目錄下。加載器會遞歸掃描 `Plugins/`，之後
`Assembly.LoadFrom` 自帶的同目錄依賴探測就會自動解析你的依賴，不會把它們混進其他插件的加載目錄裏。

掃描過程中遇到的非 .NET 檔案(比如原生 DLL `e_sqlite3.dll`)是預期之內的，會以 `Debug` 級別記錄，而不是 `Error`——只有真正加載失敗的託管程式集才會記 `Error`。

完整的真實例子可以看 `BrowserData` 插件的 `.csproj`:它就是這樣打包 `Microsoft.Data.Sqlite` 及其原生依賴 `SQLitePCLRaw`/`e_sqlite3.dll` 的，還帶了 PostBuild/PostPublish 目標，不管是哪種構建方式生成的，都會把它們歸攏進自己的子資料夾。

## 開發時自動化複製

Lertaro 自帶的插件(`CoreExtensions`、`PinyinAlias`)都在各自的 `.csproj` 裏用一個 PostBuild 目標自動化了部署步驟，把剛編譯好的 DLL 直接複製到 App 自己輸出目錄下的 `Plugins/` 資料夾，這樣重新編譯後下次啓動就能立刻生效:

```xml
<Target Name="PostBuild" AfterTargets="PostBuildEvent">
  <Copy SourceFiles="$(TargetDir)$(TargetName).dll"
        DestinationFolder="..\..\App\bin\$(Configuration)\net10.0-windows\Plugins\"
        SkipUnchangedFiles="true" />
</Target>
```

把目標路徑改成你自己的構建輸出和 Lertaro App 安裝位置實際所在的路徑即可。

## 內嵌語言包

如果插件實現了 `ITranslationProvider`(見[介面與預覽擴展](./sdk/ui-extensions))，把語言包 JSON
檔案作為內嵌資源打包，而不是散落的獨立檔案，這樣它們才會跟着 DLL 一起分發:

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Translations\**\*.json" />
</ItemGroup>
```

`TranslationService.LoadEmbeddedTranslations`(見[宿主服務](./sdk/services))會在運行時按文化名稱從程式集裏把它們讀出來。

## 版本號

給插件的 `.csproj` 加上 `<Version>`；它會顯示在**設定 → 插件**裏對應插件的卡片上，旁邊還會顯示你的插件是針對哪個 `PluginSdk` 版本編譯的——在 SDK 接口發生變化時，這對確認相容性很有用。
