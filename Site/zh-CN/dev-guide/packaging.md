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
        ├── Lertaro.Plugins.MyCustomPlugin.dll   (插件主程序集)
        ├── ThirdParty.Managed.dll              (托管第三方依赖)
        └── NativeLibrary.dll                   (原生 C/C++ 依赖——平铺放置)
```

- **依赖自动探测**：Lertaro 的程序集加载器通过 `Assembly.LoadFrom` 机制加载主 DLL，.NET 运行时会自动从该子目录中解析并加载其同级依赖库，绝不会与其他插件相互干扰。
- **原生依赖与 DLL 同级，而不是放在 `runtimes\<rid>\native` 下**：随包发布的插件工程都设置了 `<GenerateDependencyFile>false</GenerateDependencyFile>`，因此不会生成 `.deps.json`，运行时也就没有可据以解析 RID 子目录的依赖图。原生库必须能在应用程序基目录中被发现——所以要平铺复制。这也是两种架构要分别发布为独立产物、而不是合成一个安装包的原因：那个平铺的加载目录只能容纳一种架构的原生副本。
- **原生文件容错**：程序集扫描遇到非 .NET 的原生二进制文件（如 `e_sqlite3.dll`）时，加载器会以 `Debug` 级别记录后继续，而不是抛出误报的 `Error`。

## 2. 自动化构建复制配置（PostBuild）

在插件工程的 `.csproj` 文件中配置 `PostBuild` 目标，可在每次编译成功后自动把产物复制到 Lertaro 的调试目录。仓库内的插件工程实际就是这样做的——注意**平铺**的目标目录，以及**为 Service 准备的第二份复制**，正是它让别名或翻译提供者同时可用于索引器与界面：

```xml
<Target Name="PostBuild" AfterTargets="PostBuildEvent">
  <Copy SourceFiles="$(TargetDir)$(TargetName).dll"
        DestinationFolder="..\..\App\bin\$(Configuration)\net10.0-windows\Plugins\"
        SkipUnchangedFiles="true" />
  <Copy SourceFiles="$(TargetDir)$(TargetName).dll"
        DestinationFolder="..\..\Service\bin\$(Configuration)\net10.0-windows\Plugins\"
        SkipUnchangedFiles="true" />
</Target>
```

如果你的插件在加载时需要第三方托管或原生依赖，请用同级的 `<Copy>` 项把它们复制到同一个目录。

## 3. 内嵌多语言资源文件

若你的插件实现了 [`ITranslationProvider`](./sdk/ui-extensions) 多语言接口，推荐将翻译 JSON 文件作为**程序集内嵌资源**打包，避免因外部文件遗失导致界面乱码：

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Translations\**\*.json" />
</ItemGroup>
```

按 `Resources/Translations/{culture}/{type}.json` 组织文件，其中 `{type}` 就是你传给 `TranslationService.LoadEmbeddedTranslations(assembly, cultureKey, typeName)` 的 `typeName`。本仓库中每个插件都使用固定文件名 **`Plugin.json`**（`Resources/Translations/zh-CN/Plugin.json`、`Resources/Translations/en-US/Plugin.json`……）并传入 `"Plugin"`；`App.json` 并不是插件的约定——它只存在于 CoreExtensions 中，因为后者还要提供宿主自身的界面文案。语言文件夹跟随应用的七种界面语言；请求的语言没有对应文件夹时，翻译管理器会先回退到该插件自己支持的第一个语言，再回退到 `en-US`，都找不到才落到 `[key]` 占位文本。

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

## 5. Release 构建与架构产物

在 Windows 上从仓库根目录运行 `make.bat` 前，需要安装 .NET SDK 和[64 位 Inno Setup 7](https://jrsoftware.org/isdl.php#v7)（脚本会校验编译器确实是 7.x）。脚本先后调用两次 `:build_arch`：一次 `ARCH=x64`（不带 RID 发布，一如既往），一次 `ARCH=arm64`（以 `-r win-arm64 --self-contained false` 交叉发布），在 `dist/` 产出四个文件：

- `Lertaro-Setup.exe` 与 `Lertaro-Portable.zip`（x64）。
- `Lertaro-Setup-arm64.exe` 与 `Lertaro-Portable-arm64.zip`（arm64）。

两种架构之所以发布为独立产物而不是合成一个安装包，是因为 BrowserData 插件携带原生库，而那个平铺、无 `.deps.json` 的加载目录只能容纳一种架构的原生副本（见脚本头部注释）。arm64 安装包与 x64 安装包在 `Installer/installer.iss` 中的差别包括：`ArchitecturesAllowed`（`arm64` 对 `x64compatible`）、`ArchitecturesInstallIn64BitMode`、`SetupArchitecture=x64`（仅 x64 设置）、内嵌的 .NET 桌面运行时文件与下载地址、`PublishDir` 以及输出文件名；每个包内的应用程序载荷都是各自架构的原生版本。发布工作流按这四个名字计算哈希并上传，`UpdateAssetSelector` 也按名字为旧版本匹配更新通道，改名字会让已装用户失联——请保持产物命名与 `make.bat`、`Installer/installer.iss` 以及发布工作流的资源清单一致。
