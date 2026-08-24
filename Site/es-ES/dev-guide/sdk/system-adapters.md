# Adaptadores de sistema y diálogo

Este capítulo describe las interfaces de `Lertaro.PluginSdk` para acoplarse profundamente con ventanas externas (Explorador de Windows, cuadros de diálogo nativos y exploradores de archivos de terceros).

> [!NOTE]
> Las implementaciones de `IActivePathCollector`, `IFileDialogAdapter` e `IInlineSearchAdapter` se cargan en el **proceso auxiliar Hook con privilegios elevados** para sortear el aislamiento UIPI al interactuar con ventanas administradas.

## 1. Colector de ruta activa `IActivePathCollector`

Extrae el directorio de trabajo activo de la ventana en primer plano para acotar búsquedas incrustadas o resolver rutas relativas:

```csharp
namespace Lertaro.PluginSdk;

public interface IActivePathCollector
{
    string Name { get; }
    string TargetName { get; }   // Nombre del explorador (p. ej. "Directory Opus", "Total Commander")
    bool CanHandle(string className);
    string? TryGetPath(
        IntPtr activeHwnd, string activeClassName,
        IntPtr windowHwnd, string windowClassName,
        string processName);
}
```

- Pasa el control con foco (`activeHwnd`) y la ventana principal (`windowHwnd`) por separado para extraer rutas en barras de direcciones o árboles anidados.

## 2. Adaptador de diálogos nativos `IFileDialogAdapter`

Inspecciona y controla cuadros de diálogo nativos de Windows para abrir/guardar archivos:

```csharp
public interface IFileDialogAdapter
{
    string Name { get; }
    bool CanHandle(IntPtr hwnd, string className, string processName);
    string? GetCurrentPath(IntPtr hwnd);
    bool NavigateTo(IntPtr hwnd, string targetPath);
    bool TargetIsFolderOnly => false;  // Indica si solo admite carpetas (p. ej. extracción)
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => true;
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    bool RestoreFocus(IntPtr hwnd);
}
```

- **`TargetIsFolderOnly`**: Si es `true`, al seleccionar un archivo en los resultados, el anfitrión lo resuelve automáticamente a su carpeta contenedora antes de llamar a `NavigateTo`.
- **`AdapterRect`**: Contiene límites en píxeles `{ Left, Top, Right, Bottom }`.

## 3. Adaptador de búsqueda incrustada `IInlineSearchAdapter`

Incrusta la barra de búsqueda de Lertaro directamente en el diálogo o Explorador, manteniendo la sincronización bidireccional de selección:

```csharp
public interface IInlineSearchAdapter
{
    string Name { get; }
    bool IsFileExplorer => false;      // Indica si es el Explorador de Windows
    bool CanHandle(IntPtr hwnd, string className, string processName);
    bool CanTrigger(IntPtr focusedHwnd, string className);
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => CanTrigger(hwndUnderCursor, classNameUnderCursor);
    bool CanEnterActionsMode(IntPtr hwnd);
    string? GetSearchScope(IntPtr hwnd);
    bool ExecuteItem(IntPtr hwnd, string path, string searchInput);
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    IEnumerable<string> GetListItems(IntPtr hwnd) => [];
    void OnSelectionChanged(IntPtr hwnd, string path) { }
    void OnSearchFinished(IntPtr hwnd, bool executed) { }
}
```

## 4. Proveedor de Navegación rápida `IQuickNavigationProvider`

Aporta grupos y elementos dinámicos al menú contextual de [**Navegación rápida**](../../user-guide/hotkeys#3-navegacion-rapida-activadores-de-raton):

```csharp
public interface IQuickNavigationProvider
{
    string GroupName { get; }           // Título del grupo raíz
    Action<ISearchResult>? HeaderAction => null; // Botón de acción en la cabecera (p. ej. botón "+")
    string? HeaderActionTooltip => null;// ToolTip del botón de cabecera
    bool CanProvide(ISearchResult result);
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);
    void ClearSession() { }
}
```

- **`HeaderAction`**: Añade un botón en la cabecera del grupo (p. ej., "Fijar carpeta actual").
- **`DynamicMenuItem.IsHeader`**: En submenús, devolver elementos con `IsHeader = true` renderiza encabezados interactivos con botones de acción.
