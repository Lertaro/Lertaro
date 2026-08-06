# Abstracciones compartidas

Modelos y contratos de soporte usados en las interfaces del resto de páginas del SDK.

## `ISearchResult`

La vista de solo lectura de un resultado con la que opera toda interfaz de plugin — los plugins nunca reciben un
objeto de resultado mutable, solo este:

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

`Metadata` transporta Size/Created/Modified/Accessed para todo resultado que provenga del propio índice de
archivos del host — leerlo es gratis (sin E/S de disco ni IPC), a diferencia de
`FileMetadataService.GetMetadataAsync` (ver [Servicios del host](./services)), que solo merece la pena llamar
para una ruta que **no** sea ya uno de tus resultados actuales.

## `FileMetadata`

```csharp
readonly record struct FileMetadata(long Size, DateTime Created, DateTime Modified, DateTime Accessed);
```

Hora local. `default` (todos los campos a cero/`DateTime.MinValue`) significa "no disponible" — un resultado que
no proviene del índice de archivos (por ejemplo, uno generado por otro plugin). Comprueba `Metadata.Modified !=
default` para distinguir ese caso de un archivo real, legítimamente de cero bytes, cuyo `Size` es realmente `0`
pero cuyas marcas de tiempo siguen siendo reales.

## `IPluginSearchWindow`

La superficie mínima de control de ventana que se pasa a `ISearchResultAction.Execute` y callbacks similares —
deliberadamente pequeña; los plugins actúan sobre los resultados a través de esto, no reteniendo la ventana real:

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

Implementa esto junto a `IPlugin` para obtener una interfaz de configuración generada automáticamente en
**Configuración → Plugins → Configurar** — sin necesidad de WPF personalizado para los casos simples.

```csharp
interface IConfigurable
{
    PluginConfigSchema GetConfigSchema();
}
```

`PluginConfigSchema` es un `Fields: List<PluginConfigField>` plano. Cada `PluginConfigField` tiene una
`Key`, opcionalmente `GroupKey`/`LabelKey`/`DescriptionKey` (claves de traducción, resueltas a través de tu propio
`ITranslationProvider` si tienes uno), un `FieldType`, un `DefaultValue` y — según el tipo —
`Choices`, `SubFields` anidados, o `RequireModifier` (solo campos `Hotkey`, rechaza una tecla suelta sin modificador).

Define `RequireNonEmpty` en un campo (normalmente una palabra clave de activación de tipo `Text`) para volver a
`DefaultValue` en lugar de guardar un valor vacío/en blanco al persistir — de lo contrario, un usuario que borre
un campo de palabra clave dejaría en silencio inalcanzable lo que dependa de él en vez de revertir a un valor
predeterminado razonable.

`ConfigFieldType` cubre: `Boolean`, `Text`, `Integer`, `Choice`, `Array`, `Object`, `Group`,
`StringList`, `Hotkey`, `FilePath`, `FolderPath`. Ver
[CoreExtensions](../examples#coreextensions-—-acciones-y-el-menu-contextual-del-shell) para ver un esquema real
que usa grupos anidados y `StringList`.

## Registros

`ActivePathCollectorRegistry`, `FileDialogAdapterRegistry` e `InlineSearchAdapterRegistry` son la forma en que
el host reúne en tiempo de ejecución, en un solo lugar, todas las implementaciones cargadas de las
[interfaces de adaptador de sistema](./system-adapters) correspondientes. Los autores de plugins normalmente no
interactúan directamente con estos — basta con implementar la interfaz para que el host detecte y registre tu
plugin automáticamente.
