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
