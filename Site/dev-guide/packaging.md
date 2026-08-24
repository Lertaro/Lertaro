# Packaging & Distribution

This chapter details directory conventions for plugin assemblies, bundling third-party dependencies, embedding i18n JSON resources, and automated build deployment.

## 1. Plugin Assembly Directory Structure

Lertaro recursively scans the `Plugins\` folder located in the application root directory. To maintain clean isolation and avoid dependency conflicts across different plugins, place each plugin into its own dedicated subfolder:

```text
Lertaro/
├── Lertaro.App.exe
├── Lertaro.PluginSdk.dll
└── Plugins/
    └── MyCustomPlugin/
        ├── MyCustomPlugin.dll           (Main Plugin Assembly)
        ├── ThirdParty.Managed.dll       (Managed Third-Party Dependency)
        └── x64/
            └── NativeLibrary.dll        (Native C/C++ Dynamic Link Library)
```

- **Automatic Dependency Probing**: When Lertaro loads the primary DLL via `Assembly.LoadFrom`, the .NET runtime automatically probes the plugin's folder for adjacent dependencies without cross-contaminating other plugins.
- **Native File Toleration**: When non-.NET native binaries (e.g. `e_sqlite3.dll`) are encountered, the loader safely logs them at `Debug` level without throwing false positive `Error` alerts.

## 2. Automated PostBuild Copy Configuration

Add a `PostBuild` MSBuild target to your plugin's `.csproj` to automatically deploy output files into the Lertaro App debugging directory upon every successful build:

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

## 3. Embedding Localization Resources

If your plugin implements the [`ITranslationProvider`](./sdk/ui-extensions#itranslationprovider) interface, embed your translation JSON files directly as **Embedded Resources** to prevent missing language files:

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Translations\**\*.json" />
</ItemGroup>
```

Organize files using the `Resources/Translations/{CultureName}/{TypeName}.json` hierarchy (e.g. `zh-CN/MyCustomPlugin.json`, `en-US/MyCustomPlugin.json`). Calling `TranslationService.LoadEmbeddedTranslations` parses the appropriate file dynamically based on the active UI language.

## 4. Versioning & Metadata

Define assembly version numbers and descriptions inside your `.csproj`:

```xml
<PropertyGroup>
  <Version>1.2.0</Version>
  <AssemblyVersion>1.2.0.0</AssemblyVersion>
  <FileVersion>1.2.0.0</FileVersion>
  <Description>High-performance search source and context action extension plugin.</Description>
</PropertyGroup>
```

This version and description string will be presented automatically inside the **Settings → Plugins** card.
