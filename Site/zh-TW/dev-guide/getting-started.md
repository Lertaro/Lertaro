# 快速上手

## 搭建外掛專案

外掛就是一個普通的 .NET 類別庫，目標框架和宿主應用程式一致(`net10.0-windows`)，參照 `PluginSdk`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>
    <AssemblyName>YourCompany.Plugins.YourPlugin</AssemblyName>
    <Version>1.0.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <!-- 参照你 Lertaro 安裝目錄下的 Lertaro.PluginSdk.dll;如果是在 Lertaro 版本庫內部建置，
         也可以直接參照 PluginSdk.csproj。 -->
    <ProjectReference Include="..\..\PluginSdk\PluginSdk.csproj" />
  </ItemGroup>
</Project>
```

只有當外掛自己要繪製 WPF 介面(自訂預覽、佈景主題資源字典等)時才需要 `UseWPF`——純搜尋來源邏輯的外掛不需要它。

## 實作 `IPlugin`

每個外掛都有且只有一個進入點，實作 `IPlugin`:

```csharp
public class YourPlugin : IPlugin
{
    public string Name => "Your Plugin";
}
```

在此基礎上，按需實作其他介面——完整清單見[外掛 SDK 參考](./sdk/core-search-actions)。大多數真實外掛會實作 `IPlugin` 再加一兩個其他介面
(`CoreExtensionsPlugin` 實作了 `IPlugin`、`IActionProvider` 和 `IConfigurable`；參見[外掛範例](./examples))。

## 載入外掛

編譯外掛，把產生的 DLL 複製到 Lertaro App 的 `Plugins/` 資料夾(與 `Lertaro.App.exe` 同層)
——App 啟動時會掃描這個資料夾並載入找到的每一個外掛組件。這一步如何在建置時自動完成，見[封裝與發布](./packaging)。

## 偵錯

在外掛裡全程使用 `PluginSdk` 提供的 `Logger.Log(message, level)`——它的輸出會出現在
**設定 → 執行狀態** 的 **App** 記錄分頁裡，和宿主應用程式自己的記錄一樣可以按層級篩選、按關鍵字搜尋。
