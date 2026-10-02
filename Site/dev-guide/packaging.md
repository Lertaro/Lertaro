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
        ├── Lertaro.Plugins.MyCustomPlugin.dll   (Main Plugin Assembly)
        ├── ThirdParty.Managed.dll              (Managed Third-Party Dependency)
        └── NativeLibrary.dll                   (Native C/C++ Dependency — placed FLAT)
```

- **Automatic Dependency Probing**: When Lertaro loads the primary DLL via `Assembly.LoadFrom`, the .NET runtime automatically probes the plugin's folder for adjacent dependencies without cross-contaminating other plugins.
- **Native dependencies sit next to the DLL, not under `runtimes\<rid>\native`**: shipped plugin projects set `<GenerateDependencyFile>false</GenerateDependencyFile>`, so no `.deps.json` is produced and the runtime has no deps graph to resolve an RID subfolder from. A native library therefore has to be discoverable in the application base directory — copy it flat. This is also why the two architectures are published as separate artifacts rather than one combined installer: only one architecture's native copy can occupy that flat load directory.
- **Native File Toleration**: When non-.NET native binaries (e.g. `e_sqlite3.dll`) are encountered by the assembly scan, the loader logs them at `Debug` level and moves on instead of raising a false-positive `Error`.

## 2. Automated PostBuild Copy Configuration

Add a `PostBuild` MSBuild target to your plugin's `.csproj` to deploy the output into the Lertaro debug directories on every successful build. This is what the in-repo plugin projects actually do — note the **flat** destination and the **second copy for the Service**, which is what makes an alias or translation provider available to the indexer as well as to the UI:

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

Copy third-party managed or native dependencies to the same folder in a sibling `<Copy>` item if your plugin needs them at load time.

## 3. Embedding Localization Resources

If your plugin implements the [`ITranslationProvider`](./sdk/ui-extensions) interface, embed your translation JSON files directly as **Embedded Resources** to prevent missing language files:

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Translations\**\*.json" />
</ItemGroup>
```

Organize files as `Resources/Translations/{culture}/{type}.json`, where `{type}` is the `typeName` you pass to `TranslationService.LoadEmbeddedTranslations(assembly, cultureKey, typeName)`. Every plugin in this repository uses the fixed file name **`Plugin.json`** (`Resources/Translations/zh-CN/Plugin.json`, `Resources/Translations/en-US/Plugin.json`, …) and passes `"Plugin"`; `App.json` is not a plugin convention — it exists only in CoreExtensions, which also supplies the host's own UI strings. The culture folders follow the app's seven locales, and a culture with no folder simply falls back to the caller's default text.

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

## 5. Release Build & Architecture Artifacts

Run `make.bat` from the repository root on Windows with the .NET SDK and the [64-bit edition of Inno Setup 7](https://jrsoftware.org/isdl.php#v7) installed. As it stands the script invokes its build routine **once, for x64**, and produces:

- `Lertaro-Setup.exe` and `Lertaro-Portable.zip` in `dist/`.

Its header comment describes two architectures ("x64 publishes with no RID exactly as it always has; arm64 is a cross-publish") and its closing banner prints the arm64 paths, but no second `:build_arch` call sets `ARCH=x64`→`arm64`, so a local run does not create `Lertaro-Setup-arm64.exe` or `Lertaro-Portable-arm64.zip` — even though the release workflow hashes and uploads exactly those names. Keep that in mind before trusting the banner. The arm64 installer differs from the x64 one only in `ArchitecturesAllowed` (`arm64` vs `x64compatible`) plus `SetupArchitecture=x64` in `Installer/installer.iss`; the payload inside each is native to its architecture. Keep artifact names aligned with `make.bat`, `Installer/installer.iss`, and the release workflow's asset list.
