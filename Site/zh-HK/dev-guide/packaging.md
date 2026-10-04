# 打包與分發

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
        └── NativeLibrary.dll                   (原生 C/C++ 相依——平鋪放置)
```

- **相依性自動探測**：Lertaro 的組件載入器透過 `Assembly.LoadFrom` 機制載入主 DLL，.NET 執行階段會自動從該子目錄中解析並載入其同級相依庫，絕不會與其他外掛模組相互干擾。
- **原生相依要與 DLL 同層，而不是放在 `runtimes\<rid>\native`**：隨包外掛模組專案都設有 `<GenerateDependencyFile>false</GenerateDependencyFile>`，因此不會產生 `.deps.json`，執行階段也沒有 deps 相依圖可用來解析 RID 子資料夾。原生程式庫必須能在應用程式基底目錄中被發現——所以要平鋪複製。這也是兩種架構各自獨立交貨、而不是合併成單一安裝程式的原因：平鋪的載入目錄只能容納一個架構的原生複本。
- **原生檔案容錯**：組件掃描若遇到非 .NET 的原生二進位檔（如 `e_sqlite3.dll`），載入器會以 `Debug` 偵錯層級記錄後繼續，而不會產生誤報的 `Error`。

## 2. 自動化建置複製設定（PostBuild）

在外掛模組工程的 `.csproj` 檔案中設定 `PostBuild` 目標，於每次編譯成功後把產物部署到 Lertaro 的偵錯目錄。這就是倉庫內各外掛模組專案真正在做的事——請留意**平鋪**的目的地，以及**給 Service 的第二份複本**，後者正是別名或翻譯提供者能同時被索引器與 UI 使用的關鍵：

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

若你的外掛模組在載入時需要第三方託管或原生相依庫，請在同層的 `<Copy>` 項目中把它們複製到同一個資料夾。

## 3. 內嵌多語言資源檔案

若你的外掛模組實作了 [`ITranslationProvider`](./sdk/ui-extensions) 多語言介面，推薦將翻譯 JSON 檔案作為**組件內嵌資源**打包，避免因外部檔案遺失導致介面亂碼：

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Translations\**\*.json" />
</ItemGroup>
```

檔案採 `Resources/Translations/{culture}/{type}.json` 結構組織，其中 `{type}` 就是你要傳給 `TranslationService.LoadEmbeddedTranslations(assembly, cultureKey, typeName)` 的 `typeName`。本倉庫的所有外掛模組都使用固定檔名 **`Plugin.json`**（`Resources/Translations/zh-CN/Plugin.json`、`Resources/Translations/en-US/Plugin.json`……）並傳入 `"Plugin"`；`App.json` 並非外掛模組的慣例——它只存在於 CoreExtensions 中，因為該外掛模組也一併提供宿主自身的 UI 字串。文化層級資料夾遵循應用程式的七種語系；當請求的文化沒有對應資料夾時，翻譯管理員會先回退到該外掛模組第一個支援的文化，再回退到 `en-US`，最後才使用 `[key]` 佔位符。

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

## 5. Release 建構與架構產物

在 Windows 上從儲存庫根目錄執行 `make.bat` 前，需要安裝 .NET SDK 和[64 位元 Inno Setup 7](https://jrsoftware.org/isdl.php#v7)（指令碼會驗證編譯器確實是 7.x）。它會呼叫 `:build_arch` 兩次：一次以 `ARCH=x64`（一如往常不使用 RID 發佈），一次以 `ARCH=arm64`（以 `-r win-arm64 --self-contained false` 跨架構發佈），在 `dist/` 中產出四個檔案：

- `Lertaro-Setup.exe` 與 `Lertaro-Portable.zip`（x64）。
- `Lertaro-Setup-arm64.exe` 與 `Lertaro-Portable-arm64.zip`（arm64）。

兩種架構以獨立產物而非單一合併安裝程式交付，原因是 BrowserData 外掛模組攜帶了一個原生程式庫，而它平鋪、無 `.deps.json` 的載入目錄只能容納一個架構的原生複本（見指令碼的頁首註解）。arm64 安裝程式與 x64 安裝程式在 `Installer/installer.iss` 中的差異包括：`ArchitecturesAllowed`（`arm64` 對比 `x64compatible`）、`ArchitecturesInstallIn64BitMode`、`SetupArchitecture=x64`（僅 x64 設定）、內嵌的 .NET 桌面執行階段檔案與下載 URL、`PublishDir` 以及輸出檔名；兩者內部的載荷各自是其架構的原生版本。發佈工作流程恰恰就是對這四個名稱計算雜湊並上傳，而 `UpdateAssetSelector` 依名稱比對舊版安裝——重新命名產物會讓現有使用者無法被更新比對到。請保持產物名稱與 `make.bat`、`Installer/installer.iss` 以及發佈工作流程的資產清單一致。
