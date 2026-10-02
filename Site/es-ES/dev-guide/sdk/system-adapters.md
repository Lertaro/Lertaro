# Adaptadores de sistema y diálogo

Este capítulo describe las interfaces de adaptador de `Lertaro.PluginSdk` para el acoplamiento profundo de ventanas, la extracción del directorio activo y la integración de la búsqueda incrustada en el Explorador de Windows, los cuadros de diálogo nativos de archivos y los administradores de archivos de terceros.

Las cuatro viven en `Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters` y derivan de `IPluginComponent`, que es de donde procede el `Name` que el anfitrión lista en **Configuración → Plugins**: ninguna de estas interfaces declara `Name` por sí misma.

> [!NOTE]
> El anfitrión carga las implementaciones de `IActivePathCollector`, `IFileDialogAdapter` e `IInlineSearchAdapter` en el **proceso auxiliar Hook con privilegios elevados** para sortear el aislamiento UIPI de Windows al interactuar con ventanas ejecutadas por un administrador. Por eso sus miembros deben ser baratos y no interactivos: se ejecutan en la devolución de llamada de un gancho de teclado/ratón de bajo nivel, donde cualquier bloqueo que supere el `LowLevelHooksTimeout` del sistema descarta el gancho en silencio.

## 1. Colector de carpetas abiertas `IOpenedFolderCollector`

La mitad de solo lectura del contrato: informar de cada carpeta que el gestor objetivo tenga abierta ahora mismo, que es lo que alimenta el grupo **Carpetas abiertas actualmente** de la [**Navegación rápida**](../../user-guide/hotkeys) y de la lista de búsqueda incrustada.

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

public readonly record struct OpenedFolder(string Path, IntPtr WindowHandle);

public interface IOpenedFolderCollector : IPluginComponent
{
    IReadOnlyList<OpenedFolder> GetOpenedFolders() => Array.Empty<OpenedFolder>();
}
```

Un adaptador devuelve una entrada por ventana abierta, así que un gestor con cinco pestañas reporta cinco carpetas. El registro entrega esa lista **con los duplicados intactos a propósito**: una carpeta vista por dos colectores, o por un colector dos veces, aparece dos veces; los llamadores que quieran un conjunto se deduplican por ruta ellos mismos.

## 2. Colector de ruta activa `IActivePathCollector`

`IActivePathCollector` **extiende** `IOpenedFolderCollector`: un colector que sabe nombrar la carpeta de una ventana concreta suele aportar también la lista de carpetas abiertas, y la obtiene sin coste implementando el valor heredado por defecto.

Extrae el directorio de trabajo activo de la ventana en primer plano con foco, lo que permite a Lertaro acotar búsquedas incrustadas o resolver rutas relativas:

```csharp
public interface IActivePathCollector : IOpenedFolderCollector
{
    string TargetName { get; }   // Nombre del gestor objetivo (p. ej. "Directory Opus", "Total Commander")

    // Tres sobrecargas, cada una una pregunta más gruesa que la anterior. Solo la forma por nombre
    // de clase es obligatoria; las otras delegan en ella por defecto, así que un colector que aún no
    // distingue ventanas responde bien para todos los llamadores.
    bool CanHandle(string className);
    bool CanHandle(string windowClassName, string windowTitle) => CanHandle(windowClassName);
    bool CanHandle(IntPtr windowHwnd, string windowClassName, string processName) => CanHandle(windowClassName);

    string? TryGetPath(
        IntPtr activeHwnd, string activeClassName,   // el control con foco
        IntPtr windowHwnd, string windowClassName,   // su ventana de nivel superior
        string processName);
}
```

- El control con foco y la ventana principal llegan por separado para que la ruta pueda leerse de controles anidados (barras de direcciones, vistas de árbol) y no solo de la ventana en su conjunto.
- Devuelve `null` cuando la ventana se reconoce pero su carpeta no puede resolverse en ese momento: eso no es un fallo y el anfitrión simplemente deja intacto el alcance anterior.

## 3. Adaptador de diálogos nativos `IFileDialogAdapter`

Inspecciona y controla los cuadros nativos de Windows para Abrir / Guardar / Examinar:

```csharp
public interface IFileDialogAdapter : IPluginComponent
{
    bool CanHandle(IntPtr hwnd, string className, string processName);
    string? GetCurrentPath(IntPtr hwnd);
    bool NavigateTo(IntPtr hwnd, string targetPath);

    bool TargetIsFolderOnly => false;  // Verdadero si la entrada de destino solo acepta carpetas (p. ej. extracción de archivos)
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => true;

    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);          // dónde acoplar la tarjeta

    // Sondas de colocación: dónde está el propio campo de destino del diálogo y dónde está su lista
    // de archivos. La tarjeta incrustada lee ambas para decidir dónde colgarse: bajo el campo, sobre
    // la lista, o donde quepa cuando no hay sitio debajo. Devolver false en cualquiera de ellas hace
    // que el anfitrión recurra a GetDockBounds. Las dos responden por defecto "no veo ese control".
    bool TryGetTargetFieldBounds(IntPtr hwnd, out AdapterRect bounds) { bounds = default; return false; }
    bool TryGetFileListBounds(IntPtr hwnd, out AdapterRect bounds) { bounds = default; return false; }

    bool RestoreFocus(IntPtr hwnd);
}

public struct AdapterRect   // píxeles físicos
{
    public int Left, Top, Right, Bottom;
}
```

- **`TargetIsFolderOnly`**: Con `true`, si el usuario selecciona un archivo desde los resultados de búsqueda, el anfitrión resuelve automáticamente su carpeta contenedora antes de invocar `NavigateTo`.
- **`TryGetTargetFieldBounds` / `TryGetFileListBounds`**: Solo para la colocación de la tarjeta. El posicionador prefiere colgar la tarjeta bajo el campo de destino del diálogo y usa la lista de archivos como ancla de respaldo; un diálogo cuyo adaptador no resuelva ninguna de las dos simplemente recibe el rectángulo de `GetDockBounds`.
- **`RestoreFocus`**: Devuelve el teclado al propio campo de edición del diálogo. El anfitrión la llama cuando el usuario abandona la tarjeta incrustada (`Escape`, o el atajo de invocación pulsado de nuevo con la tarjeta vacía), así que no debe activar nada más.

## 4. Adaptador de búsqueda incrustada `IInlineSearchAdapter`

Incrusta la tarjeta de búsqueda de Lertaro en los diálogos de archivos objetivo o en las ventanas del Explorador de Windows, manteniendo la sincronización bidireccional de la selección:

```csharp
public interface IInlineSearchAdapter : IPluginComponent
{
    bool IsFileExplorer => false;      // Verdadero para el Explorador de Windows

    bool CanHandle(IntPtr hwnd, string className, string processName);

    // Reconocimiento sin invocación. Por defecto es CanHandle; anúlalo cuando una ventana sea
    // claramente el anfitrión que soportas pero no deba invocar la tarjeta: p. ej. cuando el foco
    // está en una línea de comandos o en un editor de renombrado, donde escribir le pertenece al
    // anfitrión, no a Lertaro.
    bool CanRecognizeHost(IntPtr hwnd, string className, string processName) => CanHandle(hwnd, className, processName);

    bool CanTrigger(IntPtr focusedHwnd, string className);
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => CanTrigger(hwndUnderCursor, classNameUnderCursor);
    bool CanEnterActionsMode(IntPtr hwnd);

    string? GetSearchScope(IntPtr hwnd);
    bool ExecuteItem(IntPtr hwnd, string path, string searchInput);
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);

    IEnumerable<string> GetListItems(IntPtr hwnd) => Array.Empty<string>();
    void OnSelectionChanged(IntPtr hwnd, string path) { }
    void OnSearchFinished(IntPtr hwnd, bool executed) { }

    // > 0: el anfitrión reactiva la propia caja de la tarjeta ese número de milisegundos después de
    // que se asiente un cambio de selección, para anfitriones que roban el foco de vuelta mientras
    // reflejan la selección. 0 (el valor por defecto) significa que nunca lo vuelva a tomar.
    int SelectionSyncFocusReclaimDelayMs => 0;
}
```

- **`GetDockBounds`**: Devuelve los límites físicos del área de contenido usada realmente para acoplarse. El anfitrión utiliza este rectángulo del contenedor para calcular el tamaño y la posición de la búsqueda incrustada; cuando esas límites puedan resolverse, el adaptador debe devolver el área de contenido del panel activo del Explorador o del diálogo, no una ventana exterior no relacionada.
- **`CanTrigger`** es la esclusa de *cada* pulsación de tecla, así que debe responder a partir del nombre de clase que recibe: el gancho no puede permitirse ahí una ida y vuelta con UI Automation.
- **`GetListItems`**: Nombres de las filas mostradas actualmente, usados para reflejar la selección. No devolver nada es válido; varios gestores soportados reportan cadenas vacías para sus filas, y por eso el anfitrión no identifica una fila solo por su nombre.
- **`CanEnterActionsMode`**: `false` elimina por completo el menú de acciones para este anfitrión: el clic derecho, `Ctrl+O` y `→` se retiran, en lugar de abrir un panel vacío.
- **`OnSearchFinished(hwnd, executed)`**: Se llama cuando la tarjeta se cierra, indicando si realmente se ejecutó un resultado, que es cuando un anfitrión que haya tenido que reprimir su propia interfaz (consejos de información, ediciones de renombrado) debe restituirla.

## 5. Proveedor de Navegación rápida `IQuickNavigationProvider`

Aporta grupos y elementos dinámicos al [**menú de Navegación rápida**](../../user-guide/hotkeys):

```csharp
public enum MouseTriggerType { DoubleClick, MiddleClick }

public interface IQuickNavigationProvider : IPluginComponent
{
    string GroupName { get; }                                   // Título del grupo raíz
    string IPluginComponent.Name => GroupName;                   // mapeado, no escrito a mano

    Action<ISearchResult>? HeaderAction => null;                 // Botón de acción en la fila de cabecera (p. ej. "+")
    string? HeaderActionTooltip => null;                         // ToolTip de ese botón

    bool CanProvide(ISearchResult result);
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);

    // Sin valor por defecto: implementarlo es obligatorio. Desmonta lo que el menú haya dejado
    // asignado (flujos CDS del shell en caché, manejadores de icono nativos) al cerrarse el menú.
    void ClearSession();
}
```

- **`HeaderAction`**: Añade un botón de acción a la cabecera del grupo raíz (p. ej. los proveedores de marcadores agregando "Fijar carpeta actual"). El botón `+` de "guardar la carpeta en la que estás" del plugin Folder Cascader es este miembro.
- **`DynamicMenuItem.IsHeader`**: En submenús anidados, devolver elementos con `IsHeader = true` renderiza encabezados de grupo interactivos con botones de acción.
- **`MouseTriggerType`**: Nombra los dos gestos globales que pueden abrir el menú. Cuáles de ellos están activos es un ajuste del usuario, no una decisión del proveedor: consulta [**Atajos de teclado → Activadores de ratón de la Navegación rápida**](../../user-guide/settings/hotkeys-page).

## 6. Registros

El anfitrión busca los adaptadores a través de cuatro registros estáticos en `Lertaro.PluginSdk.Registries`, que es también la forma en que el componente de un plugin llega hasta el proceso Hook:

| Registro | Miembros |
| :--- | :--- |
| `ActivePathCollectorRegistry` | `Register(IActivePathCollector)`, `GetCollectors()`, `GetAllCollectors()` |
| `FileDialogAdapterRegistry` | `Register(IFileDialogAdapter)`, `GetMatchingAdapter(hwnd, className, processName)`, `GetAdapters()`, `GetAllAdapters()` |
| `InlineSearchAdapterRegistry` | `Register(IInlineSearchAdapter)`, `GetMatchingAdapter(hwnd, className, processName)`, `GetAdapters()`, `GetAllAdapters()` |
| `OpenedFolderCollectorRegistry` | `GetOpenedFolders()`: concatena lo que informa cada colector activado; los duplicados se **conservan por diseño**, y un colector que lance una excepción se omite para que un gestor de archivos averiado no hunda la instantánea entera |

Los tres primeros exponen cada uno un `Func<T, bool> FilterFunc` asignado por el anfitrión: este lo reduce a los componentes que el usuario tiene activados, así que `GetCollectors()` / `GetAdapters()` devuelven la vista filtrada mientras `GetAllCollectors()` / `GetAllAdapters()` devuelven todo lo registrado. Un plugin nunca lo asigna. El orden de coincidencia es el orden de registro, y el primer adaptador cuyo `CanHandle` responda `true` es el dueño de la ventana, que es por lo que un adaptador genérico de diálogos `#32770` no debe reclamar una ventana que un adaptador especializado ya cubre. El registro de diálogos añade además un veto: una vez que un adaptador reclama la ventana, un título de ventana presente en la lista de bloqueo hace que la búsqueda de adaptador devuelva `null` en lugar de continuar con el siguiente, de modo que ningún adaptador atiende esa ventana.
