# Shared Abstractions

Models and support contracts used across the interfaces in the other SDK pages.

## `ISearchResult`

The read-only view of a result every plugin interface operates on — plugins never get a mutable
result object, only this:

```csharp
interface ISearchResult
{
    string Name { get; }
    string FullPath { get; }
    string ContextDirectory { get; }
    bool IsDir { get; }
    bool IsApplication { get; }
    FileMetadata Metadata { get; }
    bool[]? GetHighlightMask(string text, string query);
}
```

`Metadata` carries Size/Created/Modified/Accessed for every result the host's own file index
produced — reading it is free (no disk I/O or IPC), unlike `FileMetadataService.GetMetadataAsync`
(see [Host Services](./services)), which is only worth calling for a path that **isn't** already
one of your current results.

## `FileMetadata`

```csharp
readonly record struct FileMetadata(long Size, DateTime Created, DateTime Modified, DateTime Accessed);
```

Local time. `default` (every field zero/`DateTime.MinValue`) means "not available" — a result that
isn't backed by the file index (e.g. one another plugin produced). Check `Metadata.Modified !=
default` to tell that apart from a real, legitimately zero-byte file, whose `Size` is genuinely `0`
but whose timestamps are still real.

## `IPluginSearchWindow`

The minimal window-control surface passed to `ISearchResultAction.Execute` and similar callbacks —
deliberately small; plugins act on results through this, not by holding onto the real window:

```csharp
interface IPluginSearchWindow
{
    void LocateInExplorerExternal(string path);
    void OpenFileOrFolderExternal(string path);
    void OpenFileOrFolderAsAdminExternal(string path);
    void HideWindow();
}
```

## `IConfigurable`

Implement this alongside `IPlugin` to get a configuration UI generated automatically under
**Settings → Plugins → Configure** — no custom WPF required for simple cases.

```csharp
interface IConfigurable
{
    PluginConfigSchema GetConfigSchema();
}
```

`PluginConfigSchema` is a flat `Fields: List<PluginConfigField>`. Each `PluginConfigField` has a
`Key`, optional `GroupKey`/`LabelKey`/`DescriptionKey` (translation keys, resolved through your own
`ITranslationProvider` if you have one), a `FieldType`, a `DefaultValue`, and — depending on the
type — `Choices`, nested `SubFields`, or `RequireModifier` (`Hotkey` fields only, rejects an
unmodified single key).

Set `RequireNonEmpty` on a field (typically a `Text` trigger keyword) to fall back to
`DefaultValue` instead of persisting an empty/whitespace value on save — otherwise a user clearing
a keyword field would silently make whatever depends on it unreachable rather than reverting to a
sane default.

`ConfigFieldType` covers: `Boolean`, `Text`, `Integer`, `Choice`, `Array`, `Object`, `Group`,
`StringList`, `Hotkey`, `FilePath`, `FolderPath`. See
[CoreExtensions](../examples#coreextensions-actions-and-the-shell-context-menu) for a real schema
using nested groups and `StringList`.

## Registries

`ActivePathCollectorRegistry`, `FileDialogAdapterRegistry`, and `InlineSearchAdapterRegistry` are
how the host collects every loaded implementation of the corresponding
[system adapter interfaces](./system-adapters) into one place at runtime. Plugin authors don't
normally interact with these directly — implementing the interface is enough for the host to
discover and register your plugin automatically.
