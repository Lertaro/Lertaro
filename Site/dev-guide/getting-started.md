# Getting Started

## Scaffold a plugin project

A plugin is a plain .NET class library targeting the same target framework as the host app
(`net10.0-windows`), referencing `PluginSdk`:

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
    <!-- Reference Lertaro.PluginSdk.dll from your Lertaro install directory, or PluginSdk.csproj
         directly if you're building inside the Lertaro repo itself. -->
    <ProjectReference Include="..\..\PluginSdk\PluginSdk.csproj" />
  </ItemGroup>
</Project>
```

`UseWPF` is only required if your plugin renders any WPF UI itself (a custom preview, a theme
resource dictionary, etc.) — a plugin that's pure search-provider logic doesn't need it.

## Implement `IPlugin`

Every plugin has exactly one entry point implementing `IPlugin`:

```csharp
public class YourPlugin : IPlugin
{
    public string Name => "Your Plugin";
}
```

From there, implement whichever additional interfaces your plugin actually needs — see the
[Plugin SDK Reference](./sdk/core-search-actions) for the full list. Most real plugins implement
`IPlugin` plus one or two more (`CoreExtensionsPlugin` implements `IPlugin`, `IActionProvider`, and
`IConfigurable`; see [Example Plugins](./examples)).

## Load it

Build your plugin and copy the output DLL into the Lertaro App's `Plugins/` folder next to
`Lertaro.App.exe` — the App scans that folder at startup and loads every plugin assembly it
finds. See [Packaging & Deployment](./packaging) for how the shipped plugins automate this step as
part of their own build.

## Debugging

Use `Logger.Log(message, level)` (from `PluginSdk`) throughout your plugin — its output lands in
the **App** log tab under **Settings → Service Status**, filterable by level and searchable by
keyword, exactly like the host app's own logs.
