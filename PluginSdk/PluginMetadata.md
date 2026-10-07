# Display metadata for unloaded plugins

Declare the plugin's display metadata in its project so the host can show its
name and introduction without loading the assembly or invoking plugin code:

```xml
<ItemGroup>
  <AssemblyMetadata Include="Lertaro.Plugin.NameKey" Value="Example_PluginName" />
  <AssemblyMetadata Include="Lertaro.Plugin.DescriptionKey" Value="Example_PluginDesc" />
</ItemGroup>
```

Keys refer to the same embedded JSON translations used by the plugin's runtime
properties, under `Resources/Translations/<culture>/*.json`. The host reads
English first, then overrides it with the selected interface language. Keep
these keys consistent with the plugin's `Name` and `Description` properties.

For literal text, use `Lertaro.Plugin.Name` or `Lertaro.Plugin.Description`
instead. A translated key takes precedence over a literal when both exist.
The version comes from the assembly version, so it needs no duplicate declaration.

The host uses `PEReader`; it never instantiates attributes or loads dependencies.
Older third-party DLLs without these declarations fall back to standard
`AssemblyTitle`/`AssemblyDescription` attributes, then the filename. Plugins must
be rebuilt to include new metadata declarations.

## Loading and configuration contract

- The App and per-user Hook exclude disabled `Lertaro.Plugins.*.dll` files before
  loading them. Do not reference another native plugin entry assembly: dependency
  resolution can otherwise load that DLL indirectly. Reference the host SDK with
  `Private="false"`. The shared Windows indexing service continues using aliases.
- Turning off every toggleable component disables the whole plugin. Apply saves
  a batch; closing Settings then restarts silently when activation changed. OK
  saves and closes. Until restart, component filtering is not assembly unloading.
- Use `PluginSettingsService` and configuration schemas for native parameters in
  `plugin-settings.json`. Activation remains in `user-settings.json`. The temporary
  [migration module](../Core/Settings/Migration/README.md) is the only legacy bridge;
  plugins must not add their own legacy reads or dual writes. Plugin-owned Calendar
  and Flow data stay in their existing files.
- Implement `IPlugin.PrepareForSettingsTransferAsync()` if persistent data needs
  flushing or plugin-owned external writers need stopping. Await completion and
  report errors. A no-op is appropriate when no such work exists; the host's
  process restart stops the remaining in-process code. FlowLauncherBridge already
  saves and waits for its external community runtime before transfer/restart.
- Provider-only assemblies are supported. Settings groups components by assembly;
  it uses static descriptions when no `IPlugin` description is available.

## Bundled-plugin audit, 2026-10-07

All 30 native entry projects declare a display name and description, resolve their
translation keys in all seven shipped locales (210 locale dictionaries), and
reference the host SDK without copying it. Their declarations match runtime
properties, including the provider-only PinyinAlias entry. No native plugin has a
project reference to another native entry DLL. `Flow.Launcher.Plugin` is the bridge's
compatibility API library, not a 31st native plugin.

Audited projects: AnimeThemes, AudioDeviceSelector, AutoCAD, Bandizip, BrowserData,
Calendar, ContentSearch, CoreExtensions, CuratedThemes, CustomActions, CustomCommands,
DirectoryOpus, FileFilters, Files, FileUnlocker, FlowLauncherBridge, FolderCascader,
OneCommander, PinyinAlias, ProcessManager, QuickLookBridge, SpanishAlias,
SystemSettings, TotalCommander, Translator, WebSearch, WindowSwitcher, WinRAR, WPS,
and Xyplorer.

The audit corrected SpanishAlias's missing runtime description and the loaded
provider-only card's missing description. Native parameter access uses the SDK;
the only plugin-side reference to `user-settings.json` is an explanatory comment.
Calendar, ContentSearch and BrowserData check component enablement before their
background work; complete shutdown still follows the restart contract. Flow's
restart hook saves settings, disposes plugins and waits for external JSON-RPC
sessions. This is a source and metadata audit, not an end-to-end test of every
external application integration.
