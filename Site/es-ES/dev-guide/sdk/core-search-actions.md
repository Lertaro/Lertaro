# Búsqueda central y acciones

Este capítulo describe las interfaces y estructuras principales de `Lertaro.PluginSdk` para aportar fuentes de búsqueda, respuestas de cálculo instantáneo, motores de transliteración de alias, manejadores de tokens de sufijo y menús contextuales.

## 1. Especificaciones base: `IPluginComponent` e `IPlugin`

Todos los componentes de plugins heredan de `IPluginComponent`:

```csharp
namespace Lertaro.PluginSdk;

public interface IPluginComponent
{
    string Name => GetType().Name;      // Nombre visible del componente
    string Description => string.Empty; // Descripción mostrada como ToolTip en Configuración
}

public interface IPlugin : IPluginComponent
{
    // Identificador del punto de entrada principal del plugin
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
}
```

> [!TIP]
> `GetInstantResults` se ejecuta de forma síncrona para garantizar la fluidez de escritura. Para peticiones asíncronas de red (traducción, sugerencias web), devuelve un elemento de marcador provisional, obtén los datos en segundo plano mediante `Task.Run`, almacénalos en caché y llama a `SearchRefreshService.RefreshIfMatches` para actualizar los resultados activos.

### Motor de transliteración de alias no ASCII `IAliasProvider`

Genera alias indexables para texto no ASCII, permitiendo coincidencias mixtas de pinyin y caracteres:

```csharp
public interface IAliasProvider
{
    string Name { get; }
    bool CanHandle(string text);
    IReadOnlyList<(char Start, char End)> InputRanges { get; }  // Rango de entrada (ideogramas CJK)
    IReadOnlyList<(char Start, char End)> OutputRanges { get; } // Rango de salida (a-z)
    IEnumerable<string> GetAliases(string text);

    int Version => 1;                                           // Incrementar para reindexar
    int[]? MapAliasToSourceIndices(string text, string alias) => null; // Mapeo para resaltado
    void GetAliasesUtf8(string text, AliasByteSink dest);       // Constructor UTF-8 sin asignaciones
    IEnumerable<string> GetQueryForms(string term);             // Despliegue de formas en la consulta
}
```

### Manejador de tokens de sufijo de consulta `IQueryTokenProvider`

Procesa tokens situados al final de la consulta (p. ej. `report :size`, `doc :@today`, `image ::"hello world"`), aplicando transformaciones (ordenación, filtrado) sobre los resultados:

```csharp
public interface IQueryTokenProvider : IPluginComponent
{
    bool CanHandle(string token);
    Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results);
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
public interface ISearchResultAction : IPluginComponent
{
    string GroupName { get; }           // Nombre del grupo
    string DisplayName { get; }         // Título de la acción
    string? Hotkey { get; }             // Atajo predeterminado (p. ej. "Ctrl+Shift+C")
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    ImageSource Icon { get; }           // Icono
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool CanExecute(IReadOnlyList<ISearchResult> selection);
    void Execute(IReadOnlyList<ISearchResult> selection, IPluginSearchWindow window);
}
```

### Constructor de menús dinámicos `IDynamicActionProvider`

Construye menús dinámicos en tiempo de ejecución (como integrar los menús contextuales del Shell de Windows):

```csharp
public interface IDynamicActionProvider
{
    string GroupName { get; }
    int? Priority => 0;                 // Prioridad de visualización en el menú
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    void Init() { }                     // Inicialización única en el ciclo de vida
    bool CanProvide(IReadOnlyList<ISearchResult> selection);
    IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> selection, IntPtr hMenu);
    IEnumerable<(string Hotkey, Action Execute)> GetHotkeyActions(IReadOnlyList<ISearchResult> selection);
    void ExecuteCommand(IReadOnlyList<ISearchResult> selection, uint commandId, IntPtr ownerHwnd);
    void ClearSession() { }
}
```

## 4. Estructuras auxiliares

- **`SearchableItem` / `InstantResultItem`**: Contiene `Title`, `Description`, `IconData`, `IconColor`, `ActionType`, `ActionArgument`, `TabCompletion`, `HBitmapIcon` (liberado automáticamente) y delegado `OnExecute`.
- **`DynamicMenuItem`**: Contiene `Text`, `CommandId`, `IsSeparator`, `HasSubMenu`, `SubMenuHandle`, `IsDisabled`, `OnExecute` e `IsHeader` (renderiza un encabezado de grupo con botón de acción opcional).
- **`SearchWindowType`**: Enumerador con `Main` (Ventana principal), `Quick` (Ventana rápida) e `Inline` (Diálogo de archivos incrustado).
