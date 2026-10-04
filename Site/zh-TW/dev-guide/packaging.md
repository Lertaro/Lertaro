# 封裝與分發

本章節詳細介紹 Lertaro 外掛模組組件的目錄結構規範、第三方相依庫打包、多語言 JSON 資源內嵌以及自動化建置發布流程。

## 1. 外掛模組組件目錄結構

Lertaro 在啟動時會遞迴掃描應用程式根目錄下的 `Plugins\` 資料夾。為了保持環境純淨並避免不同外掛模組之間的相依庫發生版本衝突，強烈建議為每個外掛模組建立專屬的子目錄：

```text
Lertaro/
├── Lertaro.App.exe
├── Lertaro.PluginSdk.dll
└── Plugins/
    └── MyCustomPlugin/
        ├── Lertaro.Plugins.MyCustomPlugin.dll   (外掛模組主組件)
        ├── ThirdParty.Managed.dll              (託管第三方相依)
        └── NativeLibrary.dll                   (原生 C/C++ 相依——平放)
```

- **相依性自動探測**：Lertaro 的組件載入器透過 `Assembly.LoadFrom` 機制載入主 DLL，.NET 執行階段會自動從該子目錄中解析並載入其同級相依庫，絕不會與其他外掛模組相互干擾。
- **原生相依要放在 DLL 旁邊，而不是 `runtimes\<rid>\native` 之下**：隨包的外掛模組專案都設定 `<GenerateDependencyFile>false</GenerateDependencyFile>`，因此不會產生 `.deps.json`，執行階段也沒有可供解析 RID 子資料夾的相依清單可用。原生程式庫必須能在應用程式基礎目錄中被找到——所以請平放複製。這也是兩種架構要發布成個別產物、而非合併成單一安裝包的原因：那個平放的載入目錄只能由一種架構的原生複本佔用。
- **原生檔案容錯**：當組件掃描遇到非 .NET 的原生二進位檔（如 `e_sqlite3.dll`）時，載入器會以 `Debug` 層級記錄後繼續處理，而不會拋出誤報的 `Error`。

## 2. 自動化建置複製設定（PostBuild）

在外掛模組工程的 `.csproj` 檔案中設定 `PostBuild` 目標，即可在每次編譯成功後把產物部署到 Lertaro 的偵錯目錄。以下範例正是本儲存庫內外掛模組專案實際的做法——請留意**平放**的複製目的地，以及**給 Service 的第二份複製**，別名提供者與翻譯提供者正是因此才能同時供索引器與介面使用：

```xml
<Target Name="PostBuild" AfterTargets="PostBuildEvent">
  <Copy SourceFiles="$(TargetDir)$(TargetName).dll"
        DestinationFolder="..\..\App\bin\$(Configuration)\net10.0-windows\Plugins\"
        SkipUnchangedFiles="true" />
  <Copy SourceFiles="$(TargetDir)$(TargetName).dll"
        DestinationFolder="..\..\Service\bin\$(Configuration)\net10.0-windows\Plugins\"
        SkipUnchangedFiles="true" />
</Target>
```

若你的外掛模組在載入時需要第三方託管或原生相依，請在平行的 `<Copy>` 項目中把它們複製到同一個資料夾。

## 3. 內嵌多語言資源檔案

若你的外掛模組實作了 [`ITranslationProvider`](./sdk/ui-extensions) 多語言介面，推薦將翻譯 JSON 檔案作為**組件內嵌資源**打包，避免因外部檔案遺失導致介面亂碼：

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Translations\**\*.json" />
</ItemGroup>
```

檔案請依 `Resources/Translations/{culture}/{type}.json` 組織，其中 `{type}` 就是你在呼叫 `TranslationService.LoadEmbeddedTranslations(assembly, cultureKey, typeName)` 時傳入的 `typeName`。本儲存庫的每個外掛模組都使用固定檔名 **`Plugin.json`**（`Resources/Translations/zh-CN/Plugin.json`、`Resources/Translations/en-US/Plugin.json`……）並傳入 `"Plugin"`；`App.json` 不是外掛模組的慣例——它只存在於 CoreExtensions，因為該專案也提供宿主自己的介面字串。語系資料夾對應應用程式的七種介面語言；當要求的語系沒有對應資料夾時，翻譯管理員會退回該外掛模組支援的第一個語系，再退回 `en-US`，最後才顯示 `[key]` 佔位符。

## 4. 外掛模組版本與中繼資料定義

在 `.csproj` 中定義外掛模組的版本號與組件資訊：

```xml
<PropertyGroup>
  <Version>1.2.0</Version>
  <AssemblyVersion>1.2.0.0</AssemblyVersion>
  <FileVersion>1.2.0.0</FileVersion>
  <Description>針對特定業務系統的高效能即時檢索與動作擴充外掛模組。</Description>
</PropertyGroup>
```

該版本號與描述資訊會自動呈現在 Lertaro **設定 → 外掛模組** 的管理卡片中，方便使用者和開發者直觀核驗元件版本。

## 5. Release 建置與架構產物

在 Windows 上從儲存庫根目錄執行 `make.bat` 前，需要安裝 .NET SDK 和 [64 位元 Inno Setup 7](https://jrsoftware.org/isdl.php#v7)（指令碼會驗證編譯器確實是 7.x 版）。它會呼叫 `:build_arch` 兩次：一次以 `ARCH=x64`（一如往常不帶 RID 發行），一次以 `ARCH=arm64`（以 `-r win-arm64 --self-contained false` 跨平台發行），在 `dist/` 中產生四個檔案：

- `Lertaro-Setup.exe` 與 `Lertaro-Portable.zip`（x64）。
- `Lertaro-Setup-arm64.exe` 與 `Lertaro-Portable-arm64.zip`（arm64）。

兩種架構之所以發布成個別產物、而非合併成單一安裝包，是因為 BrowserData 外掛模組攜帶了一個原生程式庫，而它那個平放、沒有 `.deps.json` 的載入目錄只能容納一種架構的原生複本（見指令碼的檔頭註解）。arm64 安裝包與 x64 安裝包在 `Installer/installer.iss` 中的差異在於：`ArchitecturesAllowed`（`arm64` 對比 `x64compatible`）、`ArchitecturesInstallIn64BitMode`、`SetupArchitecture=x64`（僅 x64 設定）、內嵌的 .NET 桌面執行階段檔案與下載 URL、`PublishDir`，以及輸出檔名；每個安裝包內的應用程式載荷都原生於各自的架構。發行工作流程會對這四個檔名計算雜湊並上傳，`UpdateAssetSelector` 則按名稱比對舊版安裝——重新命名產物會讓現有使用者更新落空。請確保產物名稱與 `make.bat`、`Installer/installer.iss` 以及發行工作流程的資產清單保持一致。
