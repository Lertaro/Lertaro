# 打包与发布

## 插件是如何被发现的

App 启动时会扫描自己的 `Plugins/` 文件夹(与 `Lertaro.App.exe` 同级)里的每一个 `.dll`，查找实现了 `IPlugin` 的类型。没有单独的清单文件——程序集本身，加上它的类型实现了哪些 SDK 接口，就是完整的契约。

## 携带插件自己的依赖库

如果你的插件需要自己的托管或原生依赖 DLL(比如数据库驱动、原生互操作库……)，把它们放进 App 的
`Plugins/` 文件夹下你插件自己的一个子目录里——比如 `Plugins/YourPlugin/YourPlugin.dll` 连同它的依赖都放在同一层——而不是平铺在 `Plugins/` 根目录下。加载器会递归扫描 `Plugins/`，之后
`Assembly.LoadFrom` 自带的同目录依赖探测就会自动解析你的依赖，不会把它们混进其他插件的加载目录里。

扫描过程中遇到的非 .NET 文件(比如原生 DLL `e_sqlite3.dll`)是预期之内的，会以 `Debug` 级别记录，而不是 `Error`——只有真正加载失败的托管程序集才会记 `Error`。

完整的真实例子可以看 `BrowserData` 插件的 `.csproj`:它就是这样打包 `Microsoft.Data.Sqlite` 及其原生依赖 `SQLitePCLRaw`/`e_sqlite3.dll` 的，还带了 PostBuild/PostPublish 目标，不管是哪种构建方式生成的，都会把它们归拢进自己的子文件夹。

## 开发时自动化复制

Lertaro 自带的插件(`CoreExtensions`、`PinyinAlias`)都在各自的 `.csproj` 里用一个 PostBuild 目标自动化了部署步骤，把刚编译好的 DLL 直接复制到 App 自己输出目录下的 `Plugins/` 文件夹，这样重新编译后下次启动就能立刻生效:

```xml
<Target Name="PostBuild" AfterTargets="PostBuildEvent">
  <Copy SourceFiles="$(TargetDir)$(TargetName).dll"
        DestinationFolder="..\..\App\bin\$(Configuration)\net10.0-windows\Plugins\"
        SkipUnchangedFiles="true" />
</Target>
```

把目标路径改成你自己的构建输出和 Lertaro App 安装位置实际所在的路径即可。

## 内嵌语言包

如果插件实现了 `ITranslationProvider`(见[界面与预览扩展](./sdk/ui-extensions))，把语言包 JSON
文件作为内嵌资源打包，而不是散落的独立文件，这样它们才会跟着 DLL 一起分发:

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Translations\**\*.json" />
</ItemGroup>
```

`TranslationService.LoadEmbeddedTranslations`(见[宿主服务](./sdk/services))会在运行时按文化名称从程序集里把它们读出来。

## 版本号

给插件的 `.csproj` 加上 `<Version>`；它会显示在**设置 → 插件**里对应插件的卡片上，旁边还会显示你的插件是针对哪个 `PluginSdk` 版本编译的——在 SDK 接口发生变化时，这对确认兼容性很有用。
