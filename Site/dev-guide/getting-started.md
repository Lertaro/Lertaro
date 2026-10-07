# Getting Started

This chapter walks you through creating a native C# plugin for Lertaro from scratch, implementing core interfaces, and testing it locally.

## 1. Setting Up the Plugin Project

A Lertaro plugin is a standard .NET 10 class library project. Create a new C# class library and configure the `.csproj` file as follows:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <!-- Enable UseWPF only if your plugin renders custom XAML/WPF controls -->
    <UseWPF>true</UseWPF>
    <!-- MANDATORY prefix: the loader skips any DLL whose assembly name does not
         start with "Lertaro.Plugins." (case-insensitive) -->
    <AssemblyName>Lertaro.Plugins.MyCustomPlugin</AssemblyName>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <!-- Reference the SDK from the installed Lertaro application folder. In this
         repository the shipped plugins use a <ProjectReference> to PluginSdk/ instead. -->
    <Reference Include="Lertaro.PluginSdk">
      <HintPath>C:\Program Files\Lertaro\Lertaro.PluginSdk.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
  <ItemGroup>
    <AssemblyMetadata Include="Lertaro.Plugin.Name" Value="My Custom Plugin" />
    <AssemblyMetadata Include="Lertaro.Plugin.Description" Value="A sample plugin demonstrating Lertaro SDK integration." />
  </ItemGroup>
</Project>
```

> [!WARNING]
> The App and per-user Hook select `Lertaro.Plugins.*.dll` filenames and exclude disabled assemblies before loading or reflecting over them. Keep this prefix in `AssemblyName` and do not bundle another copy of the SDK. Avoid dependencies on other native plugin entry DLLs: the standard assembly resolver can otherwise load a disabled dependency.

> [!TIP]
> Pure logic plugins (such as search providers, alias engines, or CLI helpers) do not require `<UseWPF>`.

## 2. Implementing the `IPlugin` Entry Point

Use one public `IPlugin` entry point per assembly for its description, configuration and optional restart preparation. Provider-only assemblies are also supported; PinyinAlias is an example.

```csharp
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.MyCustomPlugin;

public class MyCustomPlugin : IPlugin
{
    public string Name => "My Custom Plugin";
    public string Description => "A sample plugin demonstrating Lertaro SDK integration.";
}
```

Settings groups components by assembly. Multiple entry classes do not provide independently disableable plugins; split independently managed functionality into separate assemblies.

From here, you can implement additional SDK interfaces on the same class or on separate component classes. For instance, implement `IInstantResultProvider` to calculate dynamic answers or `IConfigurable` to provide a schema-driven configuration form.

## 3. Deployment & Loading

1. Build your project to produce `Lertaro.Plugins.MyCustomPlugin.dll`.
2. Place the compiled DLL (along with any third-party dependencies) under the `Plugins\` folder of the Lertaro App root. A dedicated subfolder per plugin is the convention and the scan is recursive (`Plugins\**\*.dll`), so `Plugins\MyCustomPlugin\` works; the in-repo build automation drops the DLLs flat into `Plugins\` and that works too.
3. Start or restart Lertaro. The App loads enabled plugin assemblies; disabled plugins are listed from static metadata.
4. Navigate to **Settings → Plugins** to inspect your active components and settings.

> [!NOTE]
> If your plugin contributes an `IAliasProvider` or an `ITranslationProvider`, the background **Service** needs the same DLL: those two kinds are also loaded there so indexed rows carry the right aliases and labels. That is why the shipped plugin projects copy their output into both `App\bin\...\Plugins\` and `Service\bin\...\Plugins\`.

## 4. Debugging & Logging

Use `Logger` (note: it lives in the root `Lertaro.PluginSdk` namespace, not in `Lertaro.PluginSdk.Services`) for all application logging inside your plugin:

```csharp
using Lertaro.PluginSdk;

Logger.Log("Plugin initialized successfully and mounted services.", LogLevel.Info);
```

- Output appears in real-time under **Settings → Service Status → App Tab**.
- Filter logs directly by severity (Error / Warn / Info / Debug) and perform instant keyword searches to streamline development.
