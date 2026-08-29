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
        ├── MyCustomPlugin.dll           (外掛模組主組件)
        ├── ThirdParty.Managed.dll       (託管第三方相依)
        └── x64/
            └── NativeLibrary.dll        (原生 C/C++ 動態連結庫)
```

- **相依性自動探測**：Lertaro 的組件載入器透過 `Assembly.LoadFrom` 機制載入主 DLL，.NET 執行階段會自動從該子目錄中解析並載入其同級相依庫，絕不會與其他外掛模組相互干擾。
- **原生檔案容錯**：掃描過程中若遇到原生 DLL（如 `e_sqlite3.dll`）或非託管資源，載入器會以 `Debug` 偵錯層級記錄並安全跳過，絕不產生誤報 `Error` 報錯。

## 2. 自動化建置複製設定（PostBuild）

在外掛模組工程的 `.csproj` 檔案中設定 `PostBuild` 目標，可以在每次編譯成功後自動將產物複製到 Lertaro App 的 `Plugins/` 偵錯目錄下，實現即改即測：

```xml
<Target Name="PostBuild" AfterTargets="PostBuildEvent">
  <ItemGroup>
    <PluginOutputFiles Include="$(TargetDir)**\*.*" />
  </ItemGroup>
  <Copy SourceFiles="@(PluginOutputFiles)"
        DestinationFolder="..\..\App\bin\$(Configuration)\net10.0-windows\Plugins\$(TargetName)\%(RecursiveDir)"
        SkipUnchangedFiles="true" />
</Target>
```

## 3. 內嵌多語言資源檔案

若你的外掛模組實作了 [`ITranslationProvider`](./sdk/ui-extensions#itranslationprovider) 多語言介面，推薦將翻譯 JSON 檔案作為**組件內嵌資源**打包，避免因外部檔案遺失導致介面亂碼：

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Translations\**\*.json" />
</ItemGroup>
```

JSON 檔案組織建議遵循 `Resources/Translations/{CultureName}/{TypeName}.json` 規範（例如 `zh-CN/MyCustomPlugin.json`、`en-US/MyCustomPlugin.json`）。在程式碼中直接呼叫 `TranslationService.LoadEmbeddedTranslations` 即可自動按目前系統介面語言解析。

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

在 Windows 上從儲存庫根目錄執行 `make.bat` 前，需要安裝 .NET SDK 和 [64 位元 Inno Setup 7](https://jrsoftware.org/isdl.php#v7)。指令碼會分別為 x64 和 `win-arm64` 建立發行目錄，並在 `dist/` 中產生以下檔案：

- x64：`Lertaro-Setup.exe` 與 `Lertaro-Portable.zip`。
- ARM64：`Lertaro-Setup-arm64.exe` 與 `Lertaro-Portable-arm64.zip`。

ARM64 產物中的應用程式本體是原生 ARM64。ARM64 安裝包使用相容的 Inno Setup 引導程式，x64 安裝包則使用 64 位元 Inno Setup 7 外殼程式。請確保架構對應的應用程式載荷和檔名後綴在 `make.bat`、`Installer/installer.iss` 與發行工作流程中保持一致。
