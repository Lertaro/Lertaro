# Búsqueda central y acciones

Este capítulo describe las interfaces y estructuras principales de `Lertaro.PluginSdk` para aportar fuentes de búsqueda, respuestas de cálculo instantáneo, motores de transliteración de alias, manejadores de tokens de sufijo y menús contextuales.

## 1. Especificaciones base: `IPluginComponent` e `IPlugin`

Todos los componentes de plugins heredan de `IPluginComponent`:

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IPluginComponent
{
    string Name => GetType().Name;      // Nombre visible del componente
    string Description => string.Empty; // Descripción mostrada como ToolTip en Configuración
}

public interface IPlugin : IPluginComponent
{
    // Punto de entrada principal del ensamblado del plugin. Los dos miembros de sitio web
    // son opcionales: con WebsiteUrl en null la tarjeta de Configuración no muestra
    // enlace alguno.
    string? WebsiteUrl => null;
    string? WebsiteLabel => null;
}
```

## 2. Aportación de resultados de búsqueda

### Proveedor de elementos estáticos indexables `ISearchableItemProvider`

Adecuado para conjuntos de datos relativamente estáticos que no cambian con cada pulsación (p. ej. accesos del Menú Inicio, marcadores, elementos del Panel de control):

```csharp
public interface ISearchableItemProvider : IPluginComponent
{
    bool EnableAlias => true;           // Permite la transliteración de alias (p. ej. pinyin)
    event Action? ItemsChanged;         // Se dispara al cambiar los datos para reindexar
    IEnumerable<SearchableItem> GetSearchableItems();
}
```

### Proveedor de cálculo dinámico instantáneo `IInstantResultProvider`

Se ejecuta de forma sincrónica con cada pulsación de tecla, ideal para resultados derivados de la propia consulta (calculadoras, conversores, saltos a URLs):

```csharp
public interface IInstantResultProvider : IPluginComponent
{
    IEnumerable<InstantResultItem> GetInstantResults(string query);
    bool[]? GetHighlightMask(string text, string query) => null; // Máscara de resaltado

    // Las palabras que invocan a este proveedor, que el propio paso de recorte del anfitrión
    // lee para que su recorte y tu coincidencia no puedan desviarse. Ver la sección 6,
    // «Palabras disparadoras».
    IReadOnlyList<string> QueryTriggerKeywords => [];
}
```

> [!TIP]
> `GetInstantResults` se ejecuta de forma síncrona para garantizar la fluidez de escritura. Para peticiones asíncronas de red (traducción, sugerencias web), devuelve un elemento de marcador provisional, obtén los datos en segundo plano mediante `Task.Run`, almacénalos en caché y llama a `SearchRefreshService.RefreshIfMatches` para actualizar los resultados activos.

### Motor de transliteración de alias no ASCII `IAliasProvider`

Genera alias indexables para texto no ASCII, permitiendo coincidencias mixtas de pinyin y caracteres:

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IAliasProvider : IPluginComponent   // El nombre viene de IPluginComponent
{
    bool CanHandle(string text);
    IReadOnlyList<(char Start, char End)> InputRanges { get; }  // Rango de entrada (ideogramas CJK)
    IReadOnlyList<(char Start, char End)> OutputRanges { get; } // Rango de salida (a-z)

    // Se define para un motor multisilábico (pinyin): el carácter que une las sílabas en el alias
    // generado. '\0' (el valor por defecto) significa que el motor no emite separador alguno.
    char SyllableSeparator => '\0';

    IEnumerable<string> GetAliases(string text);

    int Version => 1;                                           // Incrementar para reindexar
    IEnumerable<string> GetQueryForms(string term) => Array.Empty<string>(); // Despliegue de formas en la consulta
    int[]? MapAliasToSourceIndices(string text, string alias) => null;       // Mapeo para resaltado

    // Constructor UTF-8 sin asignaciones para la ruta caliente del indexador. La implementación por
    // defecto reenvía GetAliases() al destino, pasando a minúsculas solo los alias que contienen
    // letras mayúsculas, así que un motor solo anula esto cuando puede producir bytes más baratos
    // que cadenas.
    void GetAliasesUtf8(string text, AliasByteSink dest);
}
```

### Manejador de tokens de sufijo de consulta `IQueryTokenProvider`

Procesa tokens situados al final de la consulta (p. ej. `report :size`, `doc :@today`, `image ::"hello world"`), aplicando transformaciones (ordenación, filtrado) sobre los resultados:

```csharp
public interface IQueryTokenProvider : IPluginComponent
{
    bool CanHandle(string token);
    Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results);

    // El texto que debe seguir resaltado en las filas de resultados una vez que este token se ha
    // consumido de la consulta. Null (el valor por defecto) deja intacto el resaltado del anfitrión.
    string? GetHighlightText(string token) => null;
}
```

## 3. Acciones contextuales sobre resultados

### Contenedor de proveedores de acciones `IActionProvider`

```csharp
public interface IActionProvider
{
    IEnumerable<ISearchResultAction> GetActions();
    IEnumerable<IDynamicActionProvider> GetDynamicActionProviders();
}
```

### Contrato de acción estática `ISearchResultAction`

Define una operación estática independiente (Copiar ruta, Ejecutar como Administrador) visible en el menú `Ctrl+O` o asignada a atajos:

```csharp
namespace Lertaro.PluginSdk.Abstractions;

public interface ISearchResultAction : Plugins.IPluginComponent
{
    string GroupName { get; }           // Encabezado del grupo en el menú de acciones
    string DisplayName { get; }         // Título de la acción
    // Las acciones se identifican por su nombre visible, así que Name se mapea en lugar de escribirse:
    string Plugins.IPluginComponent.Name => DisplayName;

    // No nulo, con valor por defecto. La cadena vacía significa "sin atajo", que es como las acciones
    // destructivas de archivos quedaron sin asignar antes de recibir de vuelta los acordes del
    // Explorador.
    string Hotkey => string.Empty;
    IReadOnlyList<string> Keywords => Array.Empty<string>();
    IReadOnlyList<string> Parameters => Array.Empty<string>();

    // Dónde aparece la acción. Por defecto: visible en la búsqueda, y visible en el menú solo cuando
    // no reclama palabra clave (una palabra clave es como una acción aparece como fila).
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => true;
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => Keywords.Count == 0;

    ImageSource? Icon { get; }          // Icono de la acción; null dibuja el predeterminado del grupo
    bool CanExecute(IReadOnlyList<ISearchResult> results);
    void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view);
}
```

### Constructor de menús dinámicos `IDynamicActionProvider`

Construye menús dinámicos en tiempo de ejecución (como integrar los menús contextuales del Shell de Windows):

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IDynamicActionProvider : IPluginComponent
{
    string GroupName { get; }
    string IPluginComponent.Name => GroupName;    // mismo mapeo que ISearchResultAction

    int Priority => 0;                            // Peso de ordenación del menú, no anulable
    IReadOnlyList<string> Keywords => Array.Empty<string>();
    IReadOnlyList<string> Parameters => Array.Empty<string>();

    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => true;
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => Keywords.Count == 0;

    void Init() { }                               // Calentamiento único, se dispara con la primera apertura del menú
    bool CanProvide(IReadOnlyList<ISearchResult> results);

    // Opta por los menús sobre resultados instantáneos (un título de ventana, una fila de proceso).
    // Por defecto false, porque la mayoría de proveedores se basan en rutas de archivos que esas
    // filas no tienen.
    bool CanProvideForInstantResults => false;

    IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> results, IntPtr hMenu);
    IEnumerable<(string Hotkey, Action Execute)> GetHotkeyActions(IReadOnlyList<ISearchResult> results)
        => Array.Empty<(string, Action)>();
    void ExecuteCommand(IReadOnlyList<ISearchResult> results, uint commandId, IntPtr ownerHwnd);

    // SIN implementación por defecto: implementarlo es obligatorio. Se llama al desmontarse el menú,
    // para que un proveedor que retenga manejadores nativos o un flujo CDS del shell en caché pueda
    // liberarlos.
    void ClearSession();
}
```

## 4. Estructuras auxiliares

- **`SearchableItem`**: Contiene `Title`, `Description`, `IconData`, `IconColor`, `ActionType` (`"Copy"` / `"Execute"` / `"None"`), `ActionArgument`, `TabCompletion`, `HBitmapIcon` (liberado automáticamente por el anfitrión), `ResultKind` (una etiqueta elegida por el plugin sobre la que pueden apoyarse los filtros y las columnas del anfitrión) y dos devoluciones de llamada de ejecución: `OnExecute` (`Action`) para fire-and-forget, u `OnExecuteFunc` (`Func<bool>`) cuando la acción necesita informar de si tuvo éxito; el anfitrión usa esa respuesta para decidir, por ejemplo, si cierra la ventana. `InstantResultItem` lleva los mismos miembros de presentación y de devolución de llamada **salvo `ResultKind`**, que solo existe en el modelo de elemento consultable.
- **`DynamicMenuItem`**: Contiene `Text`, `CommandId`, `IsSeparator`, `HasSubMenu`, `SubMenuHandle`, `IsDisabled`, `OnExecute`, `IsActionable` (por defecto `true`; `false` marca una fila que solo abre un submenú), `HBitmapItem` (un manejador de icono nativo del menú del Shell que se está reflejando), `ShortcutHint` (la letra con la que coincide una tecla mnemotécnica), `IsContinuation` (un cursor de paginación: este lote continúa un menú que el anfitrión aún está rellenando, y el anfitrión sigue preguntando mientras esté activo) e `IsHeader` (se renderiza como encabezado de grupo con un botón de acción opcional).
- **`SearchWindowType`**: Enumerador con `Main` (Ventana principal), `Quick` (Ventana rápida) e `Inline` (Diálogo de archivos incrustado).

## 5. Alcances de búsqueda con nombre `ISearchScopeProvider`

Un **alcance** es un prefijo de palabra clave más un conjunto de directorios: escribir `tf report` hace que el anfitrión ejecute su búsqueda normal por índice sobre `report`, restringida a esas carpetas. Es un filtro de segunda etapa sobre el índice existente, no un segundo motor de búsqueda.

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface ISearchScopeProvider : IPluginComponent
{
    // Se consulta en cada despacho de pulsación: devuelve una lista en caché, reconstruida solo
    // cuando cambia la configuración. El anfitrión ignora los alcances con palabra clave en blanco
    // o sin carpetas.
    IReadOnlyList<SearchScope> GetSearchScopes();
}

public sealed class SearchScope
{
    public string Keyword { get; init; } = string.Empty;            // primer token sin distinguir mayúsculas, p. ej. "tf"
    public IReadOnlyList<string> Folders { get; init; } = Array.Empty<string>();
    public string FilterPattern { get; init; } = "*";               // comodines Win32 separados por ';' sobre NOMBRES de archivo
}
```

A diferencia de `ISearchableItemProvider`, un proveedor de alcances nunca enumera ni materializa archivos, así que la memoria y el coste por pulsación permanecen planos por grandes que sean las carpetas configuradas. Una carpeta que ningún índice del anfitrión cubre se omite con un aviso registrado en lugar de recorrerse en vivo, que es la misma regla que siguen los ayudantes de indexación: primero debe cubrir esa carpeta un índice configurado de unidad local, de red o de carpeta. Los directorios siempre superan el `FilterPattern`.

La implementación del propio repositorio es el plugin File Filters.

## 6. Palabras disparadoras `TriggerWord`

Cualquier funcionalidad que el usuario invoque escribiendo una palabra inicial —las `QueryTriggerKeywords` de un proveedor instantáneo, las `Keywords` de una acción, la `Keyword` de un alcance o el disparador de un filtro de archivos— resuelve esa palabra a través de `Lertaro.PluginSdk.Services.TriggerWord`, de modo que el recorte del anfitrión y la coincidencia del plugin no puedan desviarse entre sí.

| Ayudante | Coincide |
| :--- | :--- |
| `string Normalize(string? configured)` | Lleva una palabra configurada a la forma que toda comparación espera: espacios exteriores recortados, vacía para `null` o en blanco. Normaliza al leer: el anfitrión recorta la palabra que elimina, así que comparar un valor sin recortar no reconoce nada mientras el anfitrión sigue quitando la palabra de la búsqueda de archivos. |
| `bool TryMatch(string query, string? word, out string argument)` | El primer token **es igual** a la palabra (sin distinguir mayúsculas). `argument` es el texto recortado que queda, vacío cuando la consulta no es más que la palabra, lo que también coincide. Una palabra más larga que simplemente empiece por ella no coincide (`csreport` no es `cs`). |
| `bool TryMatchInvoked(...)` | Igual que lo anterior, pero la palabra solo cuenta cuando algo ha sido escrito realmente después. Úsala cuando la palabra sola pondría en pantalla filas que nadie pidió: `cs` a secas sigue siendo una búsqueda de archivos, `cs ` es el proveedor. |
| `bool TryMatchAny(string query, IReadOnlyList<string> words, out string matchedWord, out string argument)` | La primera palabra que coincide gana, en orden de lista, e informa **cuál** coincidió: un motor de búsqueda web con varias palabras clave por proveedor lo necesita para volver a analizar. |
| `bool IsTypedPrefixOf(string query, string? word)` | La consulta es una escritura aún inconclusa de la palabra (`m` camino de `mkdir`). Es la única rama a la que se le permite ofrecer un disparador antes de que la palabra esté completa; devuelve false en cuanto se escribe cualquier separador. |

`IInstantResultProvider.QueryTriggerKeywords` (vacío por defecto) es lo que el anfitrión lee para su propio paso de recorte, y `PluginConfigField.IsTriggerWord` marca el ajuste `Text` que guarda una palabra así, de modo que la página de Configuración pueda avisar —sin bloquear el guardado— cuando otra funcionalidad ya responde a esa misma palabra. Dos funcionalidades sobre una misma palabra pasaría por alto: la búsqueda de archivos sigue a la que se registró primero y las filas de la otra simplemente dejan de aparecer, sin nada que le diga al usuario cuál debe renombrar.
