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
</Project>
```

> [!WARNING]
> `Lertaro.Plugins.` 前綴是一道硬性篩選，而非慣例：遞迴的 `Plugins\**\*.dll` 掃描會讀取組件名稱，不以前綴開頭的 DLL 根本不會被反射，因此命名為 `YourCompany.MyPlugin.dll` 的外掛模組會默默地不被載入。請在 `AssemblyName` 中保留前綴，並將 `<Private>false</Private>` 設好，避免把 SDK 複製到你的輸出目錄。

> [!TIP]
> 純邏輯型外掛模組（如搜尋來源、別名轉寫引擎、命令列工具）無需啟用 `<UseWPF>`。

## 2. 實作外掛模組主入口 `IPlugin`

每個外掛模組組件至少包含一個實作了 `IPlugin` 介面的公開類別，載入器會建立它的執行個體作為外掛模組的主入口點：

```csharp
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.MyCustomPlugin;

public class MyCustomPlugin : IPlugin
{
    public string Name => "My Custom Plugin";
    public string Description => "這是一個示範 Lertaro 外掛模組開發的基礎範例。";
}
```

反射會找出該 DLL 中**所有**實作 `IPlugin` 的類別並各建立一個執行個體，因此同時提供兩個類別就會註冊成兩個外掛模組、出現兩張設定卡。除非你真的需要兩個項目，否則只放一個。

在此基礎上，你可以根據外掛模組的功能定位組合實作其他 SDK 介面。例如讓該類別同時實作 `IInstantResultProvider` 提供即時答案計算，或實作 `IConfigurable` 提供視覺化的參數設定表單。

## 3. 部署與載入機制

1. 編譯你的外掛模組專案產生 `Lertaro.Plugins.MyCustomPlugin.dll`。
2. 將編譯產生的 DLL（及該外掛模組所依賴的第三方庫）放入 Lertaro App 根目錄的 `Plugins\` 資料夾下。每個外掛模組一個專屬子資料夾是慣例，且掃描是遞迴的（`Plugins\**\*.dll`），所以 `Plugins\MyCustomPlugin\` 可用；倉庫內的建置自動化是把 DLL 平鋪丟進 `Plugins\`，同樣可用。
3. 啟動或重啟 Lertaro，App 程序會掃描 `Plugins\`，並載入所有名稱帶有該前綴的組件。
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
