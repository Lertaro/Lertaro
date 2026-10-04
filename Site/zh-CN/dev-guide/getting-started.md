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
    <!-- 前缀是强制要求：加载器会跳过任何程序集名不以 "Lertaro.Plugins." 开头的 DLL（不区分大小写） -->
    <AssemblyName>Lertaro.Plugins.MyCustomPlugin</AssemblyName>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <!-- 引用已安装的 Lertaro 应用目录中的 SDK。本仓库里随包发布的插件改用指向 PluginSdk/ 的 <ProjectReference> -->
    <Reference Include="Lertaro.PluginSdk">
      <HintPath>C:\Program Files\Lertaro\Lertaro.PluginSdk.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

> [!WARNING]
> `Lertaro.Plugins.` 前缀是一道硬性过滤器，而不是命名约定：递归扫描 `Plugins\**\*.dll` 时会读取程序集名，不以前缀开头的 DLL 连反射都不会做，因此名为 `YourCompany.MyPlugin.dll` 的插件会被静默忽略。请在 `AssemblyName` 中保留该前缀，并设置 `<Private>false</Private>`，以免把 SDK 复制进你的输出目录。

> [!TIP]
> 纯逻辑型插件（如搜索源、别名转写引擎、命令行工具）无需启用 `<UseWPF>`。

## 2. 实现插件主入口 `IPlugin`

每个插件程序集至少包含一个实现 `IPlugin` 接口的公开类，加载器会实例化它作为插件的入口点：

```csharp
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.MyCustomPlugin;

public class MyCustomPlugin : IPlugin
{
    public string Name => "My Custom Plugin";
    public string Description => "这是一个演示 Lertaro 插件开发的基础范例。";
}
```

反射会找出该 DLL 中**所有** `IPlugin` 实现，并各创建一个实例，因此同时暴露两个实现就会注册出两个插件、两张设置卡片。除非你确实需要两个条目，否则只放一个。

在此基础上，你可以根据插件的功能定位组合实现其他 SDK 接口。例如让该类同时实现 `IInstantResultProvider` 提供即时答案计算，或实现 `IConfigurable` 提供可视化的参数配置表单。

## 3. 部署与加载机制

1. 编译你的插件项目生成 `Lertaro.Plugins.MyCustomPlugin.dll`。
2. 将编译生成的 DLL（及该插件所依赖的第三方库）放入 Lertaro App 根目录下的 `Plugins\` 文件夹中。为每个插件建一个独立子目录是通行约定，且扫描是递归的（`Plugins\**\*.dll`），所以 `Plugins\MyCustomPlugin\` 可用；仓库内的构建自动化则把 DLL 平铺放进 `Plugins\`，这样同样有效。
3. 启动或重启 Lertaro，App 进程会扫描 `Plugins\` 目录，并加载所有程序集名带该前缀的程序集。
4. 打开**设置 → 插件**，即可在已安装列表中看到你的插件及其组件运行状态。

> [!NOTE]
> 如果你的插件提供 `IAliasProvider` 或 `ITranslationProvider`，后台的 **Service** 也需要同一个 DLL：这两类还会在 Service 中被加载，以便索引出来的行带上正确的别名与标签。这就是随包发布的插件项目会把产物同时复制到 `App\bin\...\Plugins\` 和 `Service\bin\...\Plugins\` 的原因。

## 4. 调试与日志输出

在插件代码中建议全程使用 `Logger`（注意：它位于根命名空间 `Lertaro.PluginSdk`，而不是 `Lertaro.PluginSdk.Services`）进行日志跟踪记录：

```csharp
using Lertaro.PluginSdk;

Logger.Log("插件初始化完成，已成功挂载服务。", LogLevel.Info);
```

- 输出的日志行会实时同步呈现在 Lertaro 的**设置 → 运行状态 → App 标签页**中。
- 支持直接在界面上按日志等级过滤（Error / Warn / Info / Debug）并进行全文关键词搜索，便于排查问题。
