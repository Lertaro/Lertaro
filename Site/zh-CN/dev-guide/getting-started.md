# 快速上手

## 搭建插件项目

插件就是一个普通的 .NET 类库，目标框架和宿主应用一致(`net10.0-windows`)，引用 `PluginSdk`:

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
    <!-- 引用你 Lertaro 安装目录下的 Lertaro.PluginSdk.dll;如果是在 Lertaro 仓库内部构建，
         也可以直接引用 PluginSdk.csproj。 -->
    <ProjectReference Include="..\..\PluginSdk\PluginSdk.csproj" />
  </ItemGroup>
</Project>
```

只有当插件自己要渲染 WPF 界面(自定义预览、主题资源字典等)时才需要 `UseWPF`——纯搜索源逻辑的插件不需要它。

## 实现 `IPlugin`

每个插件都有且只有一个入口点，实现 `IPlugin`:

```csharp
public class YourPlugin : IPlugin
{
    public string Name => "Your Plugin";
}
```

在此基础上，按需实现其他接口——完整列表见[插件 SDK 参考](./sdk/core-search-actions)。大多数真实插件会实现 `IPlugin` 再加一两个其他接口
(`CoreExtensionsPlugin` 实现了 `IPlugin`、`IActionProvider` 和 `IConfigurable`；参见[插件示例](./examples))。

## 加载插件

编译插件，把生成的 DLL 复制到 Lertaro App 的 `Plugins/` 文件夹(与 `Lertaro.App.exe` 同级)
——App 启动时会扫描这个文件夹并加载找到的每一个插件程序集。这一步如何在构建时自动完成，见[打包与发布](./packaging)。

## 调试

在插件里全程使用 `PluginSdk` 提供的 `Logger.Log(message, level)`——它的输出会出现在
**设置 → 运行状态** 的 **App** 日志 Tab 里，和宿主应用自己的日志一样可以按等级过滤、按关键词搜索。
