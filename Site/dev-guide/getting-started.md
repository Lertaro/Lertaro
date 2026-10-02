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
</Project>
```

> [!WARNING]
> The `Lertaro.Plugins.` prefix is a hard filter, not a convention: the recursive `Plugins\**\*.dll` scan reads the assembly name and never even reflects over a DLL that does not start with it, so a plugin named `YourCompany.MyPlugin.dll` is silently not loaded. Keep the prefix in `AssemblyName` and set `<Private>false</Private>` so the SDK is not copied into your output.

> [!TIP]
> Pure logic plugins (such as search providers, alias engines, or CLI helpers) do not require `<UseWPF>`.

## 2. Implementing the `IPlugin` Entry Point

Every plugin assembly contains at least one public class implementing `IPlugin`, which the loader instantiates as the plugin's entry point:

```csharp
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.MyCustomPlugin;

public class MyCustomPlugin : IPlugin
{
    public string Name => "My Custom Plugin";
    public string Description => "A sample plugin demonstrating Lertaro SDK integration.";
}
```

Reflection finds **every** `IPlugin` implementation in the DLL and creates one instance each, so putting two on screen registers two plugins with two settings cards. Ship one unless you genuinely want two entries.

From here, you can implement additional SDK interfaces on the same class or on separate component classes. For instance, implement `IInstantResultProvider` to calculate dynamic answers or `IConfigurable` to provide a schema-driven configuration form.

## 3. Deployment & Loading

1. Build your project to produce `Lertaro.Plugins.MyCustomPlugin.dll`.
2. Place the compiled DLL (along with any third-party dependencies) under the `Plugins\` folder of the Lertaro App root. A dedicated subfolder per plugin is the convention and the scan is recursive (`Plugins\**\*.dll`), so `Plugins\MyCustomPlugin\` works; the in-repo build automation drops the DLLs flat into `Plugins\` and that works too.
3. Start or restart Lertaro; the App process scans `Plugins\` and loads every assembly whose name carries the prefix.
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
