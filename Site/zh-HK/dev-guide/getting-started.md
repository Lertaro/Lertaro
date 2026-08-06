# 快速上手

## 搭建插件項目

插件就是一個普通的 .NET 類庫，目標框架和宿主應用一致(`net10.0-windows`)，引用 `PluginSdk`:

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
    <!-- 引用你 Lertaro 安裝目錄下的 Lertaro.PluginSdk.dll;如果是在 Lertaro 倉庫內部構建，
         也可以直接引用 PluginSdk.csproj。 -->
    <ProjectReference Include="..\..\PluginSdk\PluginSdk.csproj" />
  </ItemGroup>
</Project>
```

只有當插件自己要渲染 WPF 介面(自訂預覽、主題資源字典等)時才需要 `UseWPF`——純搜尋源邏輯的插件不需要它。

## 實現 `IPlugin`

每個插件都有且只有一個入口點，實現 `IPlugin`:

```csharp
public class YourPlugin : IPlugin
{
    public string Name => "Your Plugin";
}
```

在此基礎上，按需實現其他接口——完整列表見[插件 SDK 參考](./sdk/core-search-actions)。大多數真實插件會實現 `IPlugin` 再加一兩個其他接口
(`CoreExtensionsPlugin` 實現了 `IPlugin`、`IActionProvider` 和 `IConfigurable`；參見[插件示例](./examples))。

## 加載插件

編譯插件，把生成的 DLL 複製到 Lertaro App 的 `Plugins/` 資料夾(與 `Lertaro.App.exe` 同級)
——App 啓動時會掃描這個資料夾並加載找到的每一個插件程式集。這一步如何在構建時自動完成，見[打包與發佈](./packaging)。

## 調試

在插件裏全程使用 `PluginSdk` 提供的 `Logger.Log(message, level)`——它的輸出會出現在
**設定 → 運行狀態** 的 **App** 日誌 Tab 裏，和宿主應用自己的日誌一樣可以按等級過濾、按關鍵詞搜尋。
