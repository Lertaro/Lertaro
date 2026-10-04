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

Organize files as `Resources/Translations/{culture}/{type}.json`, where `{type}` is the `typeName` you pass to `TranslationService.LoadEmbeddedTranslations(assembly, cultureKey, typeName)`. Every plugin in this repository uses the fixed file name **`Plugin.json`** (`Resources/Translations/zh-CN/Plugin.json`, `Resources/Translations/en-US/Plugin.json`, …) and passes `"Plugin"`; `App.json` is not a plugin convention — it exists only in CoreExtensions, which also supplies the host's own UI strings. The culture folders follow the app's seven locales; when the requested culture has no folder, the translation manager falls back to the plugin's first supported culture, then to `en-US`, and only then to the `[key]` placeholder.

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

Run `make.bat` from the repository root on Windows with the .NET SDK and the [64-bit edition of Inno Setup 7](https://jrsoftware.org/isdl.php#v7) installed (the script verifies the compiler really is 7.x). It calls `:build_arch` twice: once with `ARCH=x64` (published with no RID, as it always has been) and once with `ARCH=arm64` (a cross-publish with `-r win-arm64 --self-contained false`), producing four files in `dist/`:

- `Lertaro-Setup.exe` and `Lertaro-Portable.zip` (x64).
- `Lertaro-Setup-arm64.exe` and `Lertaro-Portable-arm64.zip` (arm64).

The two architectures ship as separate artifacts rather than one combined installer because the BrowserData plugin carries a native library, and its flat, deps.json-less load directory can hold only one architecture's native copy (see the header comment in the script). The arm64 installer differs from the x64 one in `Installer/installer.iss` in `ArchitecturesAllowed` (`arm64` vs `x64compatible`), `ArchitecturesInstallIn64BitMode`, `SetupArchitecture=x64` (set only for x64), the bundled .NET desktop runtime file and download URL, `PublishDir`, and the output file name; the payload inside each is native to its architecture. The release workflow hashes and uploads exactly these four names, and `UpdateAssetSelector` matches older installs by name — renaming an artifact would strand existing users. Keep artifact names aligned with `make.bat`, `Installer/installer.iss`, and the release workflow's asset list.
