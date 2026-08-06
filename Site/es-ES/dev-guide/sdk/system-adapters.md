# Adaptadores de sistema y de diálogo

Estas interfaces permiten que un plugin integre Lertaro con *otras* ventanas — el Explorador de archivos, diálogos
nativos de selección de archivos, gestores de archivos de terceros — en lugar de solo sus propias ventanas de búsqueda.

## `IActivePathCollector`

Extrae el "directorio actual" de cualquier ventana en primer plano que esté activa, de modo que Lertaro sepa
a qué ámbito restringir una búsqueda (o contra qué resolver una acción relativa).

```csharp
interface IActivePathCollector
{
    string Name { get; }
    string TargetName { get; }   // localized name of the app/manager this targets
    bool CanHandle(string className);
    string? TryGetPath(
        IntPtr activeHwnd, string activeClassName,
        IntPtr windowHwnd, string windowClassName,
        string processName);
}
```

Tanto el elemento activo (con el foco) como su ventana contenedora se pasan por separado, ya que muchos gestores
de archivos colocan la ruta real en un control hijo (una barra de direcciones, una selección en un árbol) que no
es la propia ventana de nivel superior.

## `IFileDialogAdapter`

Lee y controla los diálogos nativos de Abrir/Guardar de Windows, de modo que Lertaro pueda incrustarse en ellos
(ver [`IInlineSearchAdapter`](#iinlinesearchadapter) más abajo) y mantenerlos sincronizados.

```csharp
interface IFileDialogAdapter
{
    string Name { get; }
    bool CanHandle(IntPtr hwnd, string className, string processName);
    string? GetCurrentPath(IntPtr hwnd);
    bool NavigateTo(IntPtr hwnd, string targetPath);
    bool TargetIsFolderOnly { get; } // default: false
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor); // default: true
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    bool RestoreFocus(IntPtr hwnd);
}
```

`TargetIsFolderOnly` es `true` para un diálogo cuyo campo de destino solo puede contener una carpeta — el
destino "extraer a" de una herramienta de archivado comprimido, por ejemplo — nunca un archivo concreto, a
diferencia del cuadro de nombre de archivo de un diálogo Abrir/Guardar. El host lo usa para decidir si un resultado
de búsqueda elegido que es un archivo necesita resolverse a su carpeta contenedora antes de llegar siquiera a
`NavigateTo`, en lugar de dejar eso a la propia `NavigateTo`: esa llamada se ejecuta en el proceso Hook elevado,
donde `File.Exists`/`Directory.Exists` no son fiables para una unidad que el usuario interactivo haya asignado sin
elevación. Déjalo en su valor por defecto `false` para cualquier diálogo con un cuadro de nombre de archivo real.

## `IInlineSearchAdapter`

Incrusta una barra de búsqueda de Lertaro directamente en un diálogo de archivos o una ventana del Explorador de
archivos de destino (la "ventana en línea" del Manual de Usuario), manteniendo la selección sincronizada en ambas
direcciones.

```csharp
interface IInlineSearchAdapter
{
    string Name { get; }
    bool IsFileExplorer { get; }   // default false
    bool CanHandle(IntPtr hwnd, string className, string processName);
    bool CanTrigger(IntPtr focusedHwnd, string className);
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor); // default: delegates to CanTrigger
    bool CanEnterActionsMode(IntPtr hwnd);
    string? GetSearchScope(IntPtr hwnd);
    bool ExecuteItem(IntPtr hwnd, string path, string searchInput);
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    IEnumerable<string> GetListItems(IntPtr hwnd);        // optional
    void OnSelectionChanged(IntPtr hwnd, string path);    // optional
    void OnSearchFinished(IntPtr hwnd, bool executed);    // optional
}
```

`AdapterRect` (compartido con `IFileDialogAdapter`) es un rectángulo `int` sencillo `{ Left, Top, Right, Bottom }`.

## `IQuickNavigationProvider`

Suministra contenido (normalmente, un menú en cascada) para la ventana emergente de Navegación rápida — ver
[Atajos → Navegación rápida](../../user-guide/hotkeys#navegacion-rapida-raton). Si la ventana emergente llega a
abrirse para un clic dado lo decide el host, no esta interfaz: cualquier ventana ya reconocida por
`IInlineSearchAdapter`/`IFileDialogAdapter` la activa por ti, así que esto es puramente una fuente de contenido.

```csharp
interface IQuickNavigationProvider
{
    string GroupName { get; }
    Action<ISearchResult>? HeaderAction => null;
    string? HeaderActionTooltip => null;
    bool CanProvide(ISearchResult result);
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);
    void ClearSession();
}
```

`GroupName` etiqueta un encabezado de sección mostrado sobre los elementos de nivel raíz propios de este proveedor,
de modo que un usuario con más de un proveedor de navegación rápida activo pueda distinguir qué entradas aportó
cada uno — el mismo papel que desempeña `IDynamicActionProvider.GroupName` para el menú de acciones.

`HeaderAction` (opcional, `null` por defecto) añade un pequeño botón a ese mismo encabezado de grupo de nivel
raíz — por ejemplo, un proveedor al estilo de marcadores podría usarlo para "añadir la carpeta actual". Se invoca
con el mismo `ISearchResult` que `GetMenuItems` recibe para el nivel raíz; `HeaderActionTooltip` establece el
tooltip del botón y se ignora cuando `HeaderAction` es `null`. Un submenú anidado (a cualquier profundidad por
debajo de la raíz) no tiene un encabezado propio renderizado por el host, así que el efecto de `HeaderAction` se
detiene en la raíz — un proveedor que quiera el mismo botón "+" en un submenú devuelve un `DynamicMenuItem` con
`IsHeader = true` (ver más abajo) como primer elemento de ese submenú, con su propio `OnExecute` desempeñando el
mismo papel.

`DynamicMenuItem` es el mismo modelo usado por
[`IDynamicActionProvider`](./core-search-actions#idynamicactionprovider), incluida su marca `IsHeader` para una
fila de encabezado a nivel de submenú.
