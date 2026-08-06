# Búsqueda y acciones principales

## `IPluginComponent` y `IPlugin`

Todos los componentes de un plugin (incluida la propia clase de entrada del plugin) deben heredar de `IPluginComponent`. Esta interfaz proporciona el nombre y la descripción del componente:

```csharp
interface IPluginComponent
{
    string Name => GetType().Name;       // Component display name, defaults to type name
    string Description => string.Empty;  // Component description/tooltip shown in settings UI
}
```

Todo plugin debe implementar la interfaz `IPlugin` (que hereda de `IPluginComponent`) como punto de entrada principal, además de cualquier otra interfaz que necesite:

```csharp
interface IPlugin : IPluginComponent
{
}
```

## Aportar resultados de búsqueda

### `ISearchableItemProvider`

Devuelve una lista completa y cacheable de elementos para incorporar al índice — para contenido estático o
lento de enumerar, pero que no cambia con cada pulsación de tecla (por ejemplo, accesos directos del menú Inicio, una lista de marcadores).

```csharp
interface ISearchableItemProvider : IPluginComponent
{
    bool EnableAlias { get; } // default true
    event Action? ItemsChanged;
    IEnumerable<SearchableItem> GetSearchableItems();
}
```

### `IInstantResultProvider`

Se ejecuta en cada pulsación de tecla y devuelve resultados directamente — para contenido con forma de consulta,
como una calculadora o un acceso directo a una URL, algo que no querrías tener indexado de antemano.

```csharp
interface IInstantResultProvider : IPluginComponent
{
    IEnumerable<InstantResultItem> GetInstantResults(string query);
    bool[]? GetHighlightMask(string text, string query); // optional match highlighting
}
```

`GetInstantResults` es únicamente síncrono — no existe una sobrecarga async/con token de cancelación. Si tus datos
necesitan una ida y vuelta por red (traducir texto, obtener sugerencias de un motor de búsqueda), devuelve de inmediato
un elemento provisional, lanza el trabajo real con `Task.Run`, guarda el resultado en caché en cuanto llegue,
y llama a `SearchRefreshService.RefreshIfMatches` (ver [Servicios del host](./services)) para que el host vuelva a
ejecutar cualquier búsqueda cuya consulta actual ahora coincidiría con tu caché — ver la obtención de sugerencias del
plugin WebSearch (`Plugins/WebSearch/WebSearchInstantProvider.cs`) para ver un ejemplo desarrollado.

### `IAliasProvider`

Genera cadenas de búsqueda adicionales para texto no ASCII — así es como funciona el alias en pinyin para
nombres de archivo en chino (ver [PinyinAlias](../examples#pinyinalias-—-alias-en-pinyin-para-nombres-de-archivo-en-chino)).

```csharp
interface IAliasProvider
{
    string Name { get; }
    bool CanHandle(string text);
    IReadOnlyList<(char Start, char End)> InputRanges { get; }
    IReadOnlyList<(char Start, char End)> OutputRanges { get; }
    IEnumerable<string> GetAliases(string text);

    int Version { get; } // default 1
    int[]? MapAliasToSourceIndices(string text, string alias); // default null
    void GetAliasesUtf8(string text, AliasByteSink dest); // default: adapts GetAliases
    IEnumerable<string> GetQueryForms(string term); // default: none
}
```

`InputRanges` y `OutputRanges` no tienen valor por defecto — todo proveedor debe declararlos.
`InputRanges` es el rango o rangos de caracteres desde los que este proveedor transcribe (por ejemplo, el bloque de
ideogramas CJK, para el pinyin); `OutputRanges` es el rango o rangos de los que están compuestos sus alias generados
(por ejemplo, minúsculas `a`-`z`). El host usa ambos conjuntamente para segmentar un término de consulta que mezcla
el alfabeto de entrada y el de salida propios de un proveedor (por ejemplo, `大cj` contra un candidato `大长今`) en
un tramo literal comparado con el propio texto del candidato y un tramo de sintaxis de alias comparado con el alias
de este proveedor, en lugar de adivinar según si son caracteres ASCII o no.

`Version`, `MapAliasToSourceIndices` y `GetAliasesUtf8` tienen todos implementación por defecto — la mayoría de
proveedores nunca necesitan tocarlos:

- **`Version`**: increméntala cuando la salida de este proveedor pueda cambiar para la misma entrada (una
  corrección de algoritmo, una regla nueva, una tabla de datos actualizada). El índice la usa para detectar que los
  alias generados previamente por este proveedor están obsoletos y necesitan regenerarse.
- **`MapAliasToSourceIndices`**: permite que una coincidencia encontrada contra un alias (por ejemplo, qué letras de
  pinyin coincidieron) se traduzca de vuelta al texto original para resaltarlo, en lugar de no resaltar nada porque la
  consulta nunca aparece literalmente en el texto sin transcribir. Devuelve `null` (el valor por defecto) si este
  alias no fue producido por este proveedor para este texto, o si no se admite la correspondencia — el host trata
  eso como "no se puede resaltar mediante este proveedor", no como un error.
- **`GetAliasesUtf8`**: variante nativa en bytes usada en la ruta de indexación masiva del host, donde los alias
  terminan almacenándose como bytes UTF-8. La implementación por defecto adapta `GetAliases`, de modo que los
  proveedores existentes siguen funcionando sin cambios; solo hay que sobrescribirla para evitar por completo la
  materialización de cadenas cuando tu proveedor genera un volumen muy alto de alias y ese coste de asignación de
  memoria realmente se nota en la práctica.
- **`GetQueryForms`**: la contraparte de `GetAliases` en el lado de la consulta — reescribe un término de
  consulta escrito por el usuario en la forma delimitada que usan los propios alias de este proveedor, de modo
  que un término escrito como una simple cadena de caracteres pueda seguir respetando una estructura que el host
  no entiende (los límites de sílaba del pinyin, por ejemplo, que es justo lo que evita que una consulta
  coincida a través de dos sílabas sin relación). No devolver nada (el valor por defecto) significa "este
  término no está en absoluto en mi alfabeto", que es lo que impide que una consulta que este proveedor no puede
  expresar llegue a alias con los que nunca debió coincidir. Se llama una vez por término y por consulta, nunca
  por candidato, así que hacer trabajo real aquí sale a cuenta — pero cada forma que devuelvas se convierte en
  una alternativa más contra la que se compara cada candidato, así que devolver muchas es lo que cuesta caro.

### `IQueryTokenProvider`

Reclama un token final de la consulta (por ejemplo, `report :size`, `report ::"hello world"` o `report ::hello\ world`) y transforma la lista de resultados que ya ha coincidido — ordenando, filtrando o componiendo de algún otro modo sobre una búsqueda normal. Admite espacios en tokens envueltos entre comillas (`"..."`, `'...'`) o con espacios escapados (`\ `).

```csharp
interface IQueryTokenProvider : IPluginComponent
{
    bool CanHandle(string token);
    Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results);
}
```

## Acciones sobre los resultados

### `IActionProvider`

El contenedor que implementa un plugin para exponer acciones tanto estáticas como dinámicas:

```csharp
interface IActionProvider
{
    IEnumerable<ISearchResultAction> GetActions();
    IEnumerable<IDynamicActionProvider> GetDynamicActionProviders();
}
```

### `ISearchResultAction`

Una única acción estática (por ejemplo, "Copiar ruta") mostrada en el menú de Acciones o en los atajos de acción
de la ventana rápida:

```csharp
interface ISearchResultAction : IPluginComponent
{
    string GroupName { get; }
    string DisplayName { get; }
    string? Hotkey { get; }              // optional default hotkey
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    ImageSource Icon { get; }
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool CanExecute(IReadOnlyList<ISearchResult> selection);
    void Execute(IReadOnlyList<ISearchResult> selection, IPluginSearchWindow window);
}
```

### `IDynamicActionProvider`

Construye elementos de menú en tiempo de ejecución en lugar de devolver una lista fija — así es como el menú
contextual real del shell de Windows (con submenús en cascada anidados) se muestra dentro del menú de Acciones de
Lertaro; ver [ShellMenuActionProvider](../examples#coreextensions-—-acciones-y-el-menu-contextual-del-shell).

```csharp
interface IDynamicActionProvider
{
    string GroupName { get; }
    int? Priority { get; }
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    void Init();
    bool CanProvide(IReadOnlyList<ISearchResult> selection);
    IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> selection, IntPtr hMenu);
    IEnumerable<(string Hotkey, Action Execute)> GetHotkeyActions(IReadOnlyList<ISearchResult> selection);
    void ExecuteCommand(IReadOnlyList<ISearchResult> selection, uint commandId, IntPtr ownerHwnd);
    void ClearSession();
}
```

El host llama a `Init()` como máximo una vez por proceso, la primera vez que se abre cualquier menú de acciones —
antes de que `CanProvide`/`GetMenuItems` se invoquen realmente para alguna selección. El host garantiza esa
condición de "como máximo una vez", así que una implementación no necesita protegerse ella misma contra llamadas
repetidas. Úsalo para una configuración inicial lenta (por ejemplo, calentar un hilo de trabajo nativo) que se
beneficie de un adelanto real, en lugar de competir contra tu propia llamada a `CanProvide`/`GetMenuItems`, que
sigue inmediatamente después sin margen propio de tiempo — no debe bloquear, así que realiza cualquier trabajo real
en un hilo en segundo plano. La implementación por defecto no hace nada.

`Priority` controla la posición de la sección propia de este proveedor entre los grupos dinámicos (por proveedor)
del menú de acciones: los valores más bajos aparecen primero, por defecto `0`. Sin embargo, solo es un valor de
reserva — un usuario puede arrastrar/reordenar estas secciones a mano en
[Configuración → General → Ventana de búsqueda completa](../../user-guide/settings/general#ventana-de-busqueda-completa),
y una sección que el usuario haya ordenado explícitamente conserva esa posición sin importar lo que devuelva `Priority`.

## Modelos de apoyo

- **`SearchableItem`** / **`InstantResultItem`** — comparten Title, Description, IconData, IconColor,
  ActionType (`"Copy"` / `"Execute"` / `"None"`), ActionArgument, TabCompletion y `HBitmapIcon` (un
  HBITMAP GDI precargado que tiene prioridad sobre IconData cuando está establecido — el host toma posesión y
  llama a DeleteObject una vez que termina con él, así que no reutilices ni liberes el handle tú mismo después;
  ver la propia captura de miniaturas de ventana del plugin Window Switcher para ver un ejemplo desarrollado).
  `SearchableItem` además tiene `OnExecute` (un delegado de invocación directa) y `ResultKind`
  (para sobrescribir, por ejemplo `"Application"`/`"File"`).
- **`DynamicMenuItem`** — Text, CommandId, IsSeparator, HasSubMenu, SubMenuHandle, IsDisabled,
  HBitmapItem, OnExecute, ShortcutHint, IsHeader. `IsHeader` renderiza el elemento como una fila de cabecera de
  sección no pulsable (como el propio nombre de grupo de un submenú de Navegación rápida) en lugar de una fila
  normal — Text es la etiqueta de la cabecera, y si `OnExecute` también está establecido, aparece un pequeño botón
  en el borde final de la cabecera que lo invoca; cualquier otro campo se ignora cuando `IsHeader` es `true`. Esto
  es el equivalente, a nivel de submenú, de
  [`IQuickNavigationProvider.HeaderAction`](./system-adapters#iquicknavigationprovider),
  que solo cubre el nivel raíz.
- **`SearchWindowType`** enum — `Main`, `Quick`, `Inline`. Permite que una acción o proveedor se comporte de
  forma distinta según en cuál de las tres ventanas (ver el
  [Manual de Usuario](../../user-guide/getting-started#las-tres-ventanas)) se esté mostrando.
