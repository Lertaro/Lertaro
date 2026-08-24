# Extensiones de interfaz y vista previa

Este capítulo describe las interfaces de `Lertaro.PluginSdk` para ampliar la barra lateral principal, añadir columnas de datos personalizadas, incorporar pestañas dinámicas en el Panel rápido, implementar vistas previas en QuickLook, extraer miniaturas y crear temas WPF o paquetes de localización.

## 1. Proveedor de filtros de la barra lateral `ISidebarFilterProvider`

Inserta árboles de filtros por categoría en la barra lateral izquierda de la ventana principal:

```csharp
namespace Lertaro.PluginSdk;

public interface ISidebarFilterProvider : IPluginComponent
{
    IEnumerable<SidebarFilterGroup> GetFilterGroups();
}

public sealed class SidebarFilterGroup
{
    public required string GroupName { get; init; }
    public required IReadOnlyList<SidebarFilterItem> FilterItems { get; init; }
}

public sealed class SidebarFilterItem
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public ImageSource? Icon { get; init; }
    public required Func<ISearchResult, bool> FilterFunc { get; init; } // Delegado de coincidencia
}
```

## 2. Proveedor de columnas personalizadas `IResultColumnProvider`

Añade columnas de datos adicionales a la vista de tabla "Detalles" de la ventana principal (p. ej. duración multimedia, líneas de código o ramas Git):

```csharp
public interface IResultColumnProvider : IPluginComponent
{
    string ColumnId { get; }
    string HeaderText { get; }
    double DefaultWidth => 120;
    double MinWidth => 40;
    bool IsVisibleByDefault => false;
    string? GetCellText(ISearchResult result);
    int Compare(ISearchResult a, ISearchResult b) => 0; // Comparador de ordenación al hacer clic en la cabecera
}
```

## 3. Proveedor de pestañas del Panel rápido `IQuickPanelTabProvider`

Aporta pestañas de trabajo dinámicas al [**Panel rápido**](../../user-guide/settings/quick-panel) bajo la barra de búsqueda rápida:

```csharp
public interface IQuickPanelTabProvider : IPluginComponent
{
    string TabId { get; }
    string Title { get; }
    string? IconPath => null;
    Task<IReadOnlyList<ISearchResult>> GetItemsAsync(CancellationToken token);

    // Lógica de recepción de arrastrar y soltar
    bool CanHandleDragOver(IDataObject data) => false;
    Task HandleDropAsync(IDataObject data, CancellationToken token) => Task.CompletedTask;

    // Soporte para reordenación mediante arrastre
    bool SupportsReorder => false;
    Task SaveOrderAsync(IReadOnlyList<ISearchResult> orderedItems) => Task.CompletedTask;

    // Contexto de acciones exclusivo para la pestaña
    DynamicActionContext CreateActionContext() => DynamicActionContext.Default;
}
```

## 4. Vista previa de archivos y miniaturas

### Proveedor de vista previa personalizada `IFilePreviewProvider`

Personaliza la representación visual de tipos de archivo específicos dentro de la ventana de QuickLook (Barra espaciadora):

```csharp
public interface IFilePreviewProvider : IPluginComponent
{
    bool CanPreview(string filePath);
    int Priority => 0;                  // Prioridad en caso de conflicto entre plugins
    FrameworkElement CreatePreviewControl(string filePath);
}
```

#### Contratos de ciclo de vida y reutilización de vistas previas

Si el control WPF `FrameworkElement` implementa las siguientes interfaces opcionales, el anfitrión optimiza el ciclo de vida:

- **`IPreviewSessionAware`**: Implementa `void OnPreviewClosed()`, disparado al cerrar la vista previa o cambiar a un archivo no compatible, permitiendo liberar reproductores, instancias de WebView2 o flujos de archivo.
- **`IReusablePreview`**: Implementa `void UpdatePreview(string filePath)`. Al navegar entre archivos similares con las teclas de flecha, el anfitrión actualiza el contenido directamente sin recrear el control, evitando parpadeos.

### Proveedor de miniaturas personalizadas `IThumbnailProvider`

Extrae miniaturas de alta resolución para formatos sin soporte nativo en el Shell (p. ej. `.blend`, `.psd`, `.dwg`):

```csharp
public interface IThumbnailProvider : IPluginComponent
{
    bool CanProvide(string filePath);
    Task<ImageSource?> GetThumbnailAsync(string filePath, int targetSize, CancellationToken token);
}
```

## 5. Temas y localización

### Proveedor de temas `IThemeProvider`

Aporta esquemas de color y diccionarios de recursos WPF personalizados:

```csharp
public interface IThemeProvider : IPluginComponent
{
    string ThemeId { get; }
    string DisplayName { get; }
    ResourceDictionary GetResourceDictionary(bool isDark);
}
```

### Proveedor de localización `ITranslationProvider`

Aporta diccionarios de traducción multilingüe dinámicos:

```csharp
public interface ITranslationProvider : IPluginComponent
{
    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```
