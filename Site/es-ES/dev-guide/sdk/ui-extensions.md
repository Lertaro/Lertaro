# Extensiones de interfaz y vista previa

Este capítulo describe las interfaces de `Lertaro.PluginSdk` para ampliar la barra lateral de la ventana de búsqueda, añadir columnas de tabla personalizadas, aportar pestañas dinámicas al Panel rápido, crear visores de vista previa de archivos y extractores de miniaturas para QuickLook, y distribuir temas WPF y paquetes de localización i18n.

Todas ellas viven en `Lertaro.PluginSdk.Abstractions.Plugins` (los proveedores de vista previa, en `…Abstractions.Plugins.Preview`) y cada una deriva de `IPluginComponent`, que aporta el `Name` que el anfitrión muestra en **Configuración → Plugins**.

## 1. Proveedor de filtros de la barra lateral `ISidebarFilterProvider`

Inserta categorías de filtro propias en la barra lateral izquierda de la ventana de búsqueda:

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface ISidebarFilterProvider : IPluginComponent
{
    IEnumerable<SidebarFilterGroup> GetFilterGroups();

    // Peso de ordenación; los valores más bajos se renderizan primero.
    int SortOrder => 100;
}

public class SidebarFilterGroup
{
    // Identificador estable opcional que el anfitrión reconoce para los grupos conocidos
    // (p. ej. "Type" para el filtro integrado de tipo de resultado). Vacío cuando el grupo es
    // por completo definición del plugin.
    public string Id { get; set; } = string.Empty;
    public string Header { get; set; } = string.Empty;
    public List<SidebarFilterItem> Items { get; set; } = new();

    // Indica si varios elementos de este grupo pueden estar activos a la vez.
    public bool AllowMultiSelect { get; set; }
}

public class SidebarFilterItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    // Dos rutas de icono, ambas conscientes del tema: IconData es un glifo dibujado con el color
    // de texto del tema activo, y IconKey nombra un recurso que el anfitrión ya posee. Deja los
    // dos en null para no mostrar ninguno.
    public string? IconData { get; set; }
    public string? IconKey { get; set; }

    // El predicado que un resultado debe satisfacer para que este elemento coincida. Por defecto
    // "no coincide con nada", así que un elemento que nunca lo define se muestra, pero nunca
    // puede seleccionar nada.
    public Func<ISearchResult, bool> MatchPredicate { get; set; } = _ => false;
}
```

Los grupos y los elementos son clases mutables, no records: rellena las propiedades que necesites y deja el resto con sus valores por defecto.

## 2. Proveedor de columnas personalizadas `IResultColumnProvider`

Añade columnas de datos adicionales a la vista de tabla "Detalles" de la ventana de Búsqueda completa (p. ej. duración multimedia, líneas de código o rama Git). El proveedor describe sus columnas una vez y responde los valores de cada celda bajo demanda:

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IResultColumnProvider : IPluginComponent
{
    IEnumerable<ResultColumnDefinition> GetColumns();
    string GetCellValue(ISearchResult result, string columnId);
}

public class ResultColumnDefinition
{
    public string ColumnId { get; set; } = string.Empty;
    public string HeaderText { get; set; } = string.Empty;
    public double Width { get; set; } = 120;

    // Opcional: oculta la columna para los resultados a los que no aplica.
    public Func<ISearchResult, bool>? VisibilityPredicate { get; set; }

    // Opcional: ordenación propia al hacer clic en la cabecera. Negativo cuando x < y, positivo
    // cuando x > y.
    public Func<ISearchResult, ISearchResult, int>? SortComparer { get; set; }

    // Opcional: doble clic sobre la celda de esta columna en la ventana completa. Si no lo
    // defines, hacer doble clic en la celda se comporta como hacerlo en cualquier otra parte
    // de la fila.
    public Action<ISearchResult>? OnDoubleClick { get; set; }
}
```

`GetCellValue` se llama durante el renderizado de la lista, así que debe ser barato: devuelve valores precalculados o lee una caché en lugar de tocar el disco.

## 3. Proveedor de pestañas del Panel rápido `IQuickPanelTabProvider`

Aporta una pestaña de trabajo dinámica al [**Panel rápido**](../../user-guide/settings/quick-panel):

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IQuickPanelTabProvider : IPluginComponent
{
    // Las entradas que hay que mostrar ahora mismo. Se llama cada vez que se invoca el panel.
    Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
```

Ese único método es todo el contrato: no hay recepción de arrastrar y soltar, reordenación ni contexto de acciones que implementar.

- El `CancellationToken` se cancela cuando el panel se cierra. Solo la lista de la propia pestaña lo observa; nada más de tu plugin cambia.
- Rellena `Metadata.Modified` de `ISearchResult` cuando la fuente conozca uno, porque la ordenación por defecto (más reciente primero) lo usa. Déjalo con su valor por defecto y las entradas conservarán el orden en que las devuelvas.
- Un proveedor que no devuelve nada no obtiene pestaña, y no hay nada que configurar para eso.
- La pestaña existe en cuanto existe el plugin, a diferencia de una carpeta que el usuario tiene que añadir. Puede cerrarse desde la tira de pestañas y reabrirse en **Configuración → Panel rápido**, lo cual es una pregunta distinta de desactivar el componente en **Configuración → Plugins** (eso impide que se cargue en absoluto).

## 4. Vista previa de archivos y miniaturas

### Proveedor de vista previa personalizada `IFilePreviewProvider`

Renderiza vistas previas dentro del panel de QuickLook, que el usuario abre con `Alt+P` o con un clic central sobre una fila previsualizable (consulta [**Acciones y vista previa**](../../user-guide/actions-and-preview)):

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins.Preview;

public interface IFilePreviewProvider : IPluginComponent
{
    // Solo desempata. Primero se aplica el orden de proveedores que configuró el usuario
    // (Configuración → General → Vista previa y miniaturas); Priority ordena dentro de él,
    // con el valor más alto primero.
    int Priority => 0;

    bool CanPreview(string path, bool isDir);
    UIElement CreatePreview(string path, bool isDir);

    // Verdadero cuando el proveedor aloja una ventana externa propia en lugar de devolver
    // contenido WPF que se disponga dentro del panel (el plugin puente de QuickLook hace esto).
    bool RendersExternally => false;
}
```

#### Contratos de ciclo de vida y reutilización de vistas previas

Cuando tu **proveedor** implemente el primer contrato de esta lista, o el `UIElement` que devuelvas implemente el segundo, el anfitrión optimiza el ciclo de vida de la vista previa:

- **`IPreviewSessionAware`** — el reparto se hace sobre el **proveedor**, no sobre el control que devolvió: `void EndPreviewSession();`. El proveedor posee una ventana externa real (un `HwndHost`, un `IPreviewHandler` nativo y su sustituto `prevhost`), no solo un control del propio proceso, así que se le indica que termine su sesión cuando se cierra la ventana propietaria y —para los proveedores que renderizan dentro del proceso— solo cuando el panel de vista previa se oculta o termina la sesión. Sin esto, la ventana del anfitrión quedaría ahí sin nada que apuntara a ella.
- **`IReusablePreview`** — el reparto se hace sobre el elemento devuelto: `bool TrySetTarget(string path, bool isDir);`. Cuando el usuario se desplaza entre archivos parecidos con las teclas de flecha, el anfitrión pide al mismo control que cambie de objetivo en lugar de destruirlo y reconstruirlo, que es lo que elimina el parpadeo. Devuelve `false` cuando el nuevo objetivo no sirve a esta instancia y el anfitrión vuelve a construir una vista previa nueva.
- **`IReceivesPreviewPanelBounds`** — `void OnPreviewPanelBoundsAvailable(int left, int top, int width, int height);`. Un proveedor que aloja su propia ventana fuera de proceso necesita conocer el rectángulo que ocupa el panel para poder asignarlo como ventana padre de la suya o posicionarse dentro de él; implementa esto para que le entreguen ese rectángulo en cuanto se conozca.

### Proveedor de miniaturas personalizadas `IThumbnailProvider`

Extrae miniaturas de formatos sin manejador nativo del Shell (`.blend`, `.psd`, `.dwg`):

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins.Preview;

public interface IThumbnailProvider : IPluginComponent
{
    // Misma regla que con las vistas previas: primero decide el orden de proveedores de
    // miniaturas configurado por el usuario, y Priority solo ordena dentro de él.
    int Priority => 0;

    bool CanProvideThumbnail(string path, bool isDir);

    // Síncrona, porque se ejecuta en la ruta de renderizado de la lista de resultados: mantenla
    // rápida. NO tienes que memoizar: el anfitrión guarda en caché lo que devuelves (por ruta para
    // un elemento físico o virtual, por extensión en caso contrario). Dos consecuencias: `size` es
    // una decisión del propio anfitrión, tomada de la lista de imágenes del Shell, así que no
    // esperes un número concreto; y a un proveedor nunca se le pregunta por una carpeta.
    ImageSource? GetThumbnail(string path, int size);
}
```

## 5. Temas y localización

### Proveedor de temas `IThemeProvider`

Aporta paletas de color y diccionarios de recursos WPF:

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IThemeProvider : IPluginComponent
{
    IEnumerable<ITheme> GetThemes();
}
```

```csharp
namespace Lertaro.PluginSdk.Abstractions;   // nota: el tema en sí está un nivel más arriba

public interface ITheme
{
    string Id { get; }
    string DisplayName { get; }
    bool IsDark { get; }
    ResourceDictionary GetResources();

    // Por debajo de 1,0 una ventana construida con el ayudante de superficies con capa del anfitrión
    // pasa a ser una ventana en capa translúcida cuyo rincón hay que pintar y recortar; con 1,0 sigue
    // opaca, la redondea el gestor de ventanas y conserva ClearType. La elección se hace una sola vez,
    // en el constructor de la ventana, porque AllowsTransparency no puede cambiar una vez existe el
    // manejador. Hoy se aplica a las ventanas de notificación; cambiar de tema con una ventana en
    // pantalla no la reconstruye.
    double WindowOpacity => 1.0;
}
```

Un proveedor puede aportar cualquier número de temas, y cada tema lleva su propia marca de claro u oscuro en lugar de que el proveedor exponga una variante oscura.

### Proveedor de localización `ITranslationProvider`

Aporta diccionarios de traducción de forma dinámica:

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface ITranslationProvider : IPluginComponent
{
    // Los códigos de cultura que este proveedor puede servir, para que el anfitrión pueda
    // ofrecerlos en Configuración antes de cargar nada. Vacío por defecto, lo que significa
    // "descubrir a partir de lo que se pida".
    IReadOnlyList<string> SupportedCultures => Array.Empty<string>();

    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```
