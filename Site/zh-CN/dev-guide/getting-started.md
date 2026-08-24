# 快速上手

本章节将带领你从零开始搭建一个 Lertaro 原生 C# 插件项目，实现核心接口并完成本地加载与调试。

## 1. 搭建插件类库工程

Lertaro 插件是一个标准的 .NET 10 类库项目。新建一个 C# 类库工程并配置 `.csproj` 文件：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <!-- 仅当插件需要直接编写自定义 XAML/WPF 界面控件时才需开启 UseWPF -->
    <UseWPF>true</UseWPF>
    <AssemblyName>YourCompany.Plugins.MyCustomPlugin</AssemblyName>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <!-- 引用 Lertaro 安装目录下的 Lertaro.PluginSdk.dll 或源码工程中的 PluginSdk.csproj -->
    <Reference Include="Lertaro.PluginSdk">
      <HintPath>..\..\App\Lertaro.PluginSdk.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

> [!TIP]
> 纯逻辑型插件（如搜索源、别名转写引擎、命令行工具）无需启用 `<UseWPF>`，仅当需要自定义预览面板或主题资源字典时才需要启用。将 `PluginSdk.dll` 的 `<Private>` 设为 `false`，可避免在编译输出中冗余复制 SDK 本身。

## 2. 实现插件主入口 `IPlugin`

每个插件程序集中必须且仅能包含一个实现了 `IPlugin` 接口的公开类作为主入口点：

```csharp
using Lertaro.PluginSdk;

namespace YourCompany.Plugins.MyCustomPlugin;

public class MyCustomPlugin : IPlugin
{
    public string Name => "My Custom Plugin";
    public string Description => "这是一个演示 Lertaro 插件开发的基础范例。";
}
```

在此基础上，你可以根据插件的功能定位组合实现其他 SDK 接口。例如让该类同时实现 `IInstantResultProvider` 提供即时答案计算，或实现 `IConfigurable` 提供可视化的参数配置表单。

## 3. 部署与加载机制

1. 编译你的插件项目生成 `YourCompany.Plugins.MyCustomPlugin.dll`。
2. 将编译生成的 DLL（及该插件所依赖的第三方库）放入 Lertaro 安装根目录下的 `Plugins\MyCustomPlugin\` 文件夹中。
3. 启动或重启 Lertaro，App 进程会自动扫描 `Plugins/` 目录并完成类型反射加载。
4. 打开**设置 → 插件**，即可在已安装列表中看到你的插件及其组件运行状态。

## 4. 调试与日志输出

在插件代码中建议全程使用 `PluginSdk.Services.Logger` 进行日志跟踪记录：

```csharp
using Lertaro.PluginSdk.Services;

Logger.Log("插件初始化完成，已成功挂载服务。", LogLevel.Info);
```

- 输出的日志行会实时同步呈现在 Lertaro 的**设置 → 运行状态 → App 标签页**中。
- 支持直接在界面上按日志等级过滤（Error / Warn / Info / Debug）并进行全文关键词搜索，极大简化了开发排查过程。
