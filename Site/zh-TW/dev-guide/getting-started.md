# 快速上手

本章節將帶領你從零開始建置一個 Lertaro 原生 C# 外掛模組專案，實作核心介面並完成本機載入與偵錯。

## 1. 建置外掛模組類別庫專案

Lertaro 外掛模組是一個標準的 .NET 10 類別庫專案。新建一個 C# 類別庫專案並設定 `.csproj` 檔案：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <!-- 僅當外掛模組需要直接編寫自訂 XAML/WPF 介面控制項時才需開啟 UseWPF -->
    <UseWPF>true</UseWPF>
    <AssemblyName>YourCompany.Plugins.MyCustomPlugin</AssemblyName>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <!-- 引用 Lertaro 安裝目錄下的 Lertaro.PluginSdk.dll 或原始碼工程中的 PluginSdk.csproj -->
    <Reference Include="Lertaro.PluginSdk">
      <HintPath>..\..\App\Lertaro.PluginSdk.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

> [!TIP]
> 純邏輯型外掛模組（如搜尋來源、別名轉寫引擎、命令列工具）無需啟用 `<UseWPF>`，僅當需要自訂預覽面板或主題資源字典時才需要啟用。將 `PluginSdk.dll` 的 `<Private>` 設為 `false`，可避免在編譯輸出中冗餘複製 SDK 本身。

## 2. 實作外掛模組主入口 `IPlugin`

每個外掛模組組件中必須且僅能包含一個實作了 `IPlugin` 介面的公開類別作為主入口點：

```csharp
using Lertaro.PluginSdk;

namespace YourCompany.Plugins.MyCustomPlugin;

public class MyCustomPlugin : IPlugin
{
    public string Name => "My Custom Plugin";
    public string Description => "這是一個示範 Lertaro 外掛模組開發的基礎範例。";
}
```

在此基礎上，你可以根據外掛模組的功能定位組合實作其他 SDK 介面。例如讓該類別同時實作 `IInstantResultProvider` 提供即時答案計算，或實作 `IConfigurable` 提供視覺化的參數設定表單。

## 3. 部署與載入機制

1. 編譯你的外掛模組專案產生 `YourCompany.Plugins.MyCustomPlugin.dll`。
2. 將編譯產生的 DLL（及該外掛模組所相依的第三方庫）放入 Lertaro 安裝根目錄下的 `Plugins\MyCustomPlugin\` 資料夾中。
3. 啟動或重啟 Lertaro，App 處理程序會自動掃描 `Plugins/` 目錄並完成類型反映載入。
4. 開啟**設定 → 外掛模組**，即可在已安裝清單中看到你的外掛模組及其元件執行狀態。

## 4. 偵錯與記錄輸出

在外掛模組程式碼中建議全程使用 `PluginSdk.Services.Logger` 進行記錄追蹤記錄：

```csharp
using Lertaro.PluginSdk.Services;

Logger.Log("外掛模組初始化完成，已成功掛載服務。", LogLevel.Info);
```

- 輸出的記錄行會即時同步呈現在 Lertaro 的**設定 → 執行狀態 → App 索引標籤頁**中。
- 支援直接在介面上按記錄等級過濾（Error / Warn / Info / Debug）並進行全文關鍵字搜尋，極大簡化了開發排查過程。
