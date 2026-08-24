# 打包与分发

本章节详细介绍 Lertaro 插件程序集的目录结构规范、第三方依赖库打包、多语言 JSON 资源内嵌以及自动化构建发布流程。

## 1. 插件程序集目录结构

Lertaro 在启动时会递归扫描应用程序根目录下的 `Plugins\` 文件夹。为了保持环境纯净并避免不同插件之间的依赖库发生版本冲突，强烈建议为每个插件创建专属的子目录：

```text
Lertaro/
├── Lertaro.App.exe
├── Lertaro.PluginSdk.dll
└── Plugins/
    └── MyCustomPlugin/
        ├── MyCustomPlugin.dll           (插件主程序集)
        ├── ThirdParty.Managed.dll       (托管第三方依赖)
        └── x64/
            └── NativeLibrary.dll        (原生 C/C++ 动态链接库)
```

- **依赖自动探测**：Lertaro 的程序集加载器通过 `Assembly.LoadFrom` 机制加载主 DLL，.NET 运行时会自动从该子目录中解析并加载其同级依赖库，绝不会与其他插件相互干扰。
- **原生文件容错**：扫描过程中若遇到原生 DLL（如 `e_sqlite3.dll`）或非托管资源，加载器会以 `Debug` 调试级别记录并安全跳过，绝不产生误报 `Error` 报错。

## 2. 自动化构建复制配置（PostBuild）

在插件工程的 `.csproj` 文件中配置 `PostBuild` 目标，可以在每次编译成功后自动将产物复制到 Lertaro App 的 `Plugins/` 调试目录下，实现即改即测：

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

## 3. 内嵌多语言资源文件

若你的插件实现了 [`ITranslationProvider`](./sdk/ui-extensions#itranslationprovider) 多语言接口，推荐将翻译 JSON 文件作为**程序集内嵌资源**打包，避免因外部文件遗失导致界面乱码：

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Translations\**\*.json" />
</ItemGroup>
```

JSON 文件组织建议遵循 `Resources/Translations/{CultureName}/{TypeName}.json` 规范（例如 `zh-CN/MyCustomPlugin.json`、`en-US/MyCustomPlugin.json`）。在代码中直接调用 `TranslationService.LoadEmbeddedTranslations` 即可自动按当前系统界面语言解析。

## 4. 插件版本与元数据定义

在 `.csproj` 中定义插件的版本号与程序集信息：

```xml
<PropertyGroup>
  <Version>1.2.0</Version>
  <AssemblyVersion>1.2.0.0</AssemblyVersion>
  <FileVersion>1.2.0.0</FileVersion>
  <Description>针对特定业务系统的高性能即时检索与动作扩展插件。</Description>
</PropertyGroup>
```

该版本号与描述信息会自动呈现在 Lertaro **设置 → 插件** 的管理卡片中，方便用户和开发者直观核验组件版本。
