# 快速上手

本章節將帶領你從零開始搭建一個 Lertaro 原生 C# 外掛模組專案，實作核心介面並完成本機載入與偵錯。

## 1. 搭建外掛模組類別庫專案

Lertaro 外掛模組是一個標準的 .NET 10 類別庫專案。新建一個 C# 類別庫專案並設定 `.csproj` 檔案：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <!-- 僅當外掛模組需要直接編寫自訂 XAML/WPF 介面控制項時才需開啟 UseWPF -->
    <UseWPF>true</UseWPF>
    <!-- 強制前綴：組件名稱不以 "Lertaro.Plugins." 開頭的 DLL，載入器一律略過（不區分大小寫） -->
    <AssemblyName>Lertaro.Plugins.MyCustomPlugin</AssemblyName>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <!-- 從已安裝的 Lertaro 應用程式目錄引用 SDK。在本倉庫中，隨包外掛模組改用指向
         PluginSdk/ 的 <ProjectReference>。 -->
    <Reference Include="Lertaro.PluginSdk">
      <HintPath>C:\Program Files\Lertaro\Lertaro.PluginSdk.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
  <ItemGroup>
    <AssemblyMetadata Include="Lertaro.Plugin.Name" Value="My Custom Plugin" />
    <AssemblyMetadata Include="Lertaro.Plugin.Description" Value="這是一個示範 Lertaro 外掛模組開發的基礎範例。" />
  </ItemGroup>
</Project>
```

> [!WARNING]
> App 與目前使用者的 Hook 依 `Lertaro.Plugins.*.dll` 檔名篩選，並在載入或反射之前排除停用的組件。請在 `AssemblyName` 中保留此前綴，且不要附帶另一份 SDK。避免參考其他原生外掛入口 DLL，否則標準組件解析器可能載入已停用的相依項。

> [!TIP]
> 純邏輯型外掛模組（如搜尋來源、別名轉寫引擎、命令列工具）無需啟用 `<UseWPF>`。

## 2. 實作外掛模組主入口 `IPlugin`

建議每個組件只提供一個公開的 `IPlugin` 入口，負責介紹、設定及選用的重新啟動準備。也支援只有 Provider 的組件，例如 PinyinAlias。

```csharp
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.MyCustomPlugin;

public class MyCustomPlugin : IPlugin
{
    public string Name => "My Custom Plugin";
    public string Description => "這是一個示範 Lertaro 外掛模組開發的基礎範例。";
}
```

設定介面依組件組織功能。多個入口類別不會形成可獨立停用的外掛；需要獨立管理的功能應拆成不同組件。

在此基礎上，你可以根據外掛模組的功能定位組合實作其他 SDK 介面。例如讓該類別同時實作 `IInstantResultProvider` 提供即時答案計算，或實作 `IConfigurable` 提供視覺化的參數設定表單。

## 3. 部署與載入機制

1. 編譯你的外掛模組專案產生 `Lertaro.Plugins.MyCustomPlugin.dll`。
2. 將編譯產生的 DLL（及該外掛模組所依賴的第三方庫）放入 Lertaro App 根目錄的 `Plugins\` 資料夾下。每個外掛模組一個專屬子資料夾是慣例，且掃描是遞迴的（`Plugins\**\*.dll`），所以 `Plugins\MyCustomPlugin\` 可用；倉庫內的建置自動化是把 DLL 平鋪丟進 `Plugins\`，同樣可用。
3. 啟動或重新啟動 Lertaro。App 載入已啟用的外掛組件；停用外掛透過靜態中繼資料顯示。
4. 開啟**設定 → 外掛模組**，即可在已安裝列表中看到你的外掛模組及其元件運行狀態。

> [!NOTE]
> 若你的外掛模組貢獻了 `IAliasProvider` 或 `ITranslationProvider`，背景 **Service** 也需要同一份 DLL：這兩種類型的元件在 Service 端同樣會被載入，好讓索引出的資料列帶著正確的別名與標籤。這就是隨包外掛模組專案會把輸出複製到 `App\bin\...\Plugins\` 與 `Service\bin\...\Plugins\` 兩處的原因。

## 4. 偵錯與記錄輸出

在外掛模組程式碼中建議全程使用 `Logger` 進行記錄追蹤記錄（注意：它位於基底命名空間 `Lertaro.PluginSdk`，而不是 `Lertaro.PluginSdk.Services`）：

```csharp
using Lertaro.PluginSdk;

Logger.Log("外掛模組初始化完成，已成功掛載服務。", LogLevel.Info);
```

- 輸出的記錄行會即時同步呈現在 Lertaro 的**設定 → 運行狀態 → App 標籤頁**中。
- 支援直接在介面上按記錄等級過濾（Error / Warn / Info / Debug）並進行全文關鍵字搜尋，便於排查問題。
