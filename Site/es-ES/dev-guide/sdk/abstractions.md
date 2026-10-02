# Abstracciones compartidas

Este capítulo resume los modelos de datos fundamentales, los contratos de solo lectura y las abstracciones de configuración basadas en esquemas de `Lertaro.PluginSdk`.

## 1. Modelo de resultado de búsqueda `ISearchResult`

Los plugins observan los resultados de búsqueda mediante la interfaz de solo lectura `ISearchResult`:

```csharp
namespace Lertaro.PluginSdk.Abstractions;

public interface ISearchResult
{
    string Name { get; }                  // Nombre visible (p. ej. "Lertaro.exe")
    string FullPath { get; }              // Ruta física absoluta
    string ContextDirectory { get; }      // Ruta de la carpeta contenedora
    bool IsDir { get; }                   // Indica si es una carpeta
    bool IsApplication { get; }           // Indica si es un ejecutable o acceso directo
    bool[]? GetHighlightMask(string text, string query) => null; // Máscara de resaltado
    FileMetadata Metadata => default;     // Metadatos de archivo de alta precisión
    string? InstantActionArgument => null; // A qué apunta un resultado instantáneo
}
```

`FullPath` es la identidad desde la que trabaja la mayoría de las acciones, así que un resultado instantáneo que actúe sobre algo que no es una ruta —`activatewindow:12345`, `kill:4321`, la carga útil de una orden personalizada— lleva ese objetivo en `InstantActionArgument`, y los proveedores lo leen allí. Sigue siendo `null` para cualquier otro tipo de fila: resultados normales de archivos y carpetas, acciones de búsqueda de plugins y entradas del historial.

> [!NOTE]
> `ISearchResult.Metadata` se inyecta directamente desde el índice en memoria. **Acceder a esta propiedad no genera E/S de disco ni llamadas IPC**. Utiliza `FileMetadataService.GetMetadataAsync` únicamente al consultar rutas externas al conjunto de resultados.

## 2. Estructura de metadatos `FileMetadata`

```csharp
public readonly record struct FileMetadata(
    long Size,
    DateTime Created,
    DateTime Modified,
    DateTime Accessed
);
```

- Las marcas de tiempo están en **Hora local**.
- `Metadata == default` indica resultados generados dinámicamente por plugins sin respaldo en el índice físico de archivos.
- `Metadata.Modified != default` permite distinguir con precisión entre metadatos no disponibles y archivos válidos de 0 bytes.

## 3. Control de la ventana anfitriona `IPluginSearchWindow`

Se proporciona en las devoluciones de llamada de acciones (como `ISearchResultAction.Execute`) para controlar la ventana anfitriona con seguridad:

```csharp
public interface IPluginSearchWindow
{
    void LocateInExplorerExternal(string path);       // Resalta el elemento en el Explorador
    void OpenFileOrFolderExternal(string path);       // Abre con la aplicación asociada
    void OpenFileOrFolderAsAdminExternal(string path);// Ejecuta con privilegios de administrador
    void HideWindow();                                // Oculta la ventana de búsqueda actual
}
```

## 4. Configuración basada en esquemas `IConfigurable`

Al implementar `IConfigurable`, Lertaro genera automáticamente el formulario nativo en **Configuración → Plugins → Configuración** sin necesidad de escribir XAML:

```csharp
public interface IConfigurable
{
    PluginConfigSchema GetConfigSchema();
}
```

### Tipos de campo admitidos `ConfigFieldType`

| Tipo de campo | Control visual y comportamiento |
| :--- | :--- |
| **`Boolean`** | Interruptor de alternancia o casilla de verificación. |
| **`Text`** | Campo de texto. `RequireNonEmpty` vuelve a `DefaultValue` cuando el usuario lo vacía; `MaxLength` limita la longitud (0 o sin definir significa sin límite); `SelectionStart` / `SelectionLength` fijan la selección inicial, basada en cero, en el editor de diálogo que se abre para este campo. |
| **`Integer`** | Control numérico con límites mínimos y máximos. |
| **`Choice`** | Selector desplegable basado en una colección `Choices` o `ChoiceOptions`. |
| **`Array`** | Un valor de lista. Con `SubFields` es una lista de **registros** renderizada como un editor maestro/detalle (un formulario anidado por entrada —la forma que usan los plugins de filtros de archivos, comandos personalizados y búsqueda web—); sin `SubFields` es una lista simple de escalares renderizada como un editor compacto de una sola columna. El SDK no asigna ningún valor por defecto a `DefaultValue` (`object?`, `null!` en la declaración), por eso todos los plugins del repositorio pasan `new List<object>()` para una lista vacía. |
| **`Object`** | Un único valor estructurado que se edita a través de sus `SubFields`, sin las opciones de lista de `Array`. |
| **`Group`** | Agrupación en tarjeta plegable con campos secundarios (`SubFields`). |
| **`StringList`** | Lista multilínea editable con adición, eliminación, reordenación y ajuste de línea visual; los saltos reales se marcan en pantalla, pero las marcas no forman parte del valor de configuración. |
| **`Hotkey`** | Grabador de teclas con `RequireModifier = true` opcional. |
| **`FilePath` / `FolderPath`** | Campo de texto con botón para abrir el diálogo nativo de Windows. |
| **`CustomControl`** | Inserta directamente un control WPF `UIElement` personalizado (también accesible mediante `CustomControl`). |
| **`Button`** | Muestra un botón de acción e invoca el delegado `OnClick` del campo; no almacena ningún valor. |

Los demás miembros de `PluginConfigField` son lo que el anfitrión muestra o persiste alrededor de esos tipos: `Key` (el nombre de la opción guardada), `GroupKey` (en qué tarjeta `Group` está el campo), `LabelKey` / `DescriptionKey` (claves de traducción, no texto literal), `RequireNonEmpty`, `Choices` / `ChoiceOptions` / `SubFields`, `IsTriggerWord` (ver [**Búsqueda central y acciones**](./core-search-actions), «Palabras disparadoras»), `MaxLength`, `SelectionStart` / `SelectionLength`, `CustomControl`, `OnClick`, y dos delegados que permiten a un plugin guardar un valor en algún sitio distinto del almacén de opciones del anfitrión: `Func<object?>? GetValue` y `Action<object?>? SetValue`.

### Campos de icono

Un campo de texto cuya clave de esquema es `Icon` se muestra con una vista previa del icono. Admite WPF Path Data directamente; al pegar un documento SVG/XML completo, el anfitrión extrae y combina todos los valores `<path d>` y guarda únicamente el WPF Path Data resultante. El contenido no válido se borra y se notifica mediante un cuadro de diálogo de error con el tema de Lertaro. Los valores vacíos siguen siendo válidos cuando no se desea ningún icono.

`PluginConfigSchema` admite delegados de ciclo de vida `OnSave` y `OnRollback`: `OnSave` se ejecuta cuando el usuario pulsa **Aceptar/Aplicar** para confirmar los cambios, mientras que `OnRollback` restaura el estado cuando se cancelan o revierten.

### Etiquetas localizadas para opciones

Usa `ChoiceOptions` cuando una opción necesite una etiqueta localizada pero deba conservar un valor de configuración estable. `PluginConfigChoice.Value` se guarda en la configuración del plugin y `LabelKey` se resuelve como el texto mostrado. Si el valor guardado y el texto mostrado son iguales, puedes seguir usando la colección `Choices` existente.

```csharp
new PluginConfigField
{
    Key = "DisplayMode",
    FieldType = ConfigFieldType.Choice,
    DefaultValue = "FriendlyName",
    ChoiceOptions =
    [
        new PluginConfigChoice
        {
            Value = "FriendlyName",
            LabelKey = "DisplayMode_FriendlyName"
        }
    ]
}
```

## 5. Resultados de archivos en la búsqueda completa `IFullSearchFileResultProvider`

Los plugins que necesiten añadir filas de archivos o carpetas reales a la ventana de búsqueda completa pueden implementar `IFullSearchFileResultProvider`:

```csharp
public interface IFullSearchFileResultProvider : IPluginComponent
{
    IReadOnlyList<InstantResultItem> GetFileResults(string query, int limit);

    // Opcional. El cuerpo por defecto recorre GetFileResults, así que un proveedor escrito antes de que
    // existiera este miembro sigue funcionando sin cambios.
    IEnumerable<InstantResultItem> GetFileResultsStreamed(string query, int limit);
}
```

El anfitrión llama al proveedor en un hilo de fondo mientras la búsqueda de archivos de la propia ventana completa aún está llegando en streaming, y pinta sus filas en cuanto aparecen en lugar de esperar a que la búsqueda termine. Devuelve una lista vacía cuando el plugin no gestiona la consulta. Cada `InstantResultItem` devuelto debe representar un archivo o carpeta existente para que las columnas de ruta, tamaño y tipo sigan siendo útiles. Un proveedor cuya respuesta tarde segundos —un recorrido del índice de texto completo, por ejemplo— puede anular `GetFileResultsStreamed` para entregar coincidencias según las encuentra, lo que muestra las primeras filas mientras aún se buscan las demás; anularlo es opcional porque el cuerpo por defecto de la interfaz recorre `GetFileResults`. Este componente tiene **su propio** interruptor de activación y desactivación en **Configuración → Plugins**, ligado a su propio tipo de componente: desactivar el proveedor de resultados instantáneos del plugin no desactiva este componente, ni a la inversa.

## 6. Resolución de rutas configuradas por el usuario `UserPathResolver`

Cuando un plugin acepta una ruta introducida por el usuario o guardada en su configuración, usa `Lertaro.PluginSdk.Helpers.UserPathResolver` para aplicar las mismas reglas de variables de entorno y rutas virtuales de Windows Shell antes de llamar a las API del sistema de archivos:

```csharp
string expanded = UserPathResolver.Expand(rawPath);            // recibe string?, devuelve string
bool isVirtual = UserPathResolver.IsVirtualPath(expanded);
string resolved = UserPathResolver.Resolve(rawPath);           // segundo argumento opcional, más abajo

// Tanto Resolve como ResolveForNavigation aceptan un Func<string, string>? opcional que convierte
// un token virtual imposible de analizar en una ruta real antes de preguntar al sistema de
// archivos; sin resolvedor y sin nada que analizar, la entrada se devuelve como último recurso.

// Usa ResolveForNavigation, no Resolve, cuando la ruta va a abrirse o recorrerse: además normaliza
// un elemento virtual del Shell a la ruta de destino al que el anfitrión puede navegar.
string target = UserPathResolver.ResolveForNavigation(rawPath);
```

`Expand` recorta los espacios exteriores y expande variables como `%USERPROFILE%`. `Resolve` realiza esa expansión y después intenta convertir tokens como `shell:Downloads` o `::{CLSID}` en una ruta física. Una carpeta virtual sin ruta física, como `shell:AppsFolder`, se resuelve en su nombre canónico `::{CLSID}`, de modo que todas sus grafías coinciden; ese resultado sigue siendo virtual. Solo un token que Shell no puede analizar en absoluto se devuelve sin cambios. Comprueba el resultado con `IsVirtualPath` antes de pasarlo a las API del sistema de archivos. Las API de indexación de directorios solo pueden enumerar una ruta cuando se resuelve en una carpeta real cubierta por el índice.
