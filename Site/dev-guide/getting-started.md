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
    <AssemblyName>YourCompany.Plugins.MyCustomPlugin</AssemblyName>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <!-- Reference Lertaro.PluginSdk.dll from your Lertaro installation directory -->
    <Reference Include="Lertaro.PluginSdk">
      <HintPath>..\..\App\Lertaro.PluginSdk.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

> [!TIP]
> Pure logic plugins (such as search providers, alias engines, or CLI helpers) do not require `<UseWPF>`. Setting `<Private>false</Private>` on `PluginSdk.dll` avoids redundant copying of the SDK into your build output.

## 2. Implementing the `IPlugin` Entry Point

Every plugin assembly must contain exactly one public class implementing the `IPlugin` interface as its primary entry point:

```csharp
using Lertaro.PluginSdk;

namespace YourCompany.Plugins.MyCustomPlugin;

public class MyCustomPlugin : IPlugin
{
    public string Name => "My Custom Plugin";
    public string Description => "A sample plugin demonstrating Lertaro SDK integration.";
}
```

From here, you can implement additional SDK interfaces on the same class or on separate component classes. For instance, implement `IInstantResultProvider` to calculate dynamic answers or `IConfigurable` to provide a schema-driven configuration form.

## 3. Deployment & Loading

1. Build your project to produce `YourCompany.Plugins.MyCustomPlugin.dll`.
2. Place the compiled DLL (along with any third-party dependencies) into a subfolder under `Plugins\MyCustomPlugin\` within the Lertaro App root.
3. Start or restart Lertaro; the App process will automatically scan `Plugins/` and load the assembly.
4. Navigate to **Settings → Plugins** to inspect your active components and settings.

## 4. Debugging & Logging

Use `PluginSdk.Services.Logger` for all application logging inside your plugin:

```csharp
using Lertaro.PluginSdk.Services;

Logger.Log("Plugin initialized successfully and mounted services.", LogLevel.Info);
```

- Output appears in real-time under **Settings → Service Status → App Tab**.
- Filter logs directly by severity (Error / Warn / Info / Debug) and perform instant keyword searches to streamline development.
