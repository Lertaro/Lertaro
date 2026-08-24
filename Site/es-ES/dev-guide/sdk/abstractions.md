# Abstracciones compartidas

Este capítulo resume los modelos de datos fundamentales, los contratos de solo lectura y las abstracciones de configuración basadas en esquemas de `Lertaro.PluginSdk`.

## 1. Modelo de resultado de búsqueda `ISearchResult`

Los plugins observan los resultados de búsqueda mediante la interfaz de solo lectura `ISearchResult`:

```csharp
namespace Lertaro.PluginSdk;

public interface ISearchResult
{
    string Name { get; }                  // Nombre visible (p. ej. "Lertaro.exe")
    string FullPath { get; }              // Ruta física absoluta
    string ContextDirectory { get; }      // Ruta de la carpeta contenedora
    bool IsDir { get; }                   // Indica si es una carpeta
    bool IsApplication { get; }           // Indica si es un ejecutable o acceso directo
    FileMetadata Metadata { get; }        // Metadatos de archivo de alto rendimiento
    bool[]? GetHighlightMask(string text, string query); // Máscara de resaltado
}
```

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
| **`Text`** | Campo de texto. Admite `RequireNonEmpty` para volver a `DefaultValue` si se vacía. |
| **`Integer`** | Control numérico con límites mínimos y máximos. |
| **`Choice`** | Selector desplegable basado en una colección `Choices`. |
| **`Hotkey`** | Grabador de teclas con `RequireModifier = true` opcional. |
| **`FilePath` / `FolderPath`** | Campo de texto con botón para abrir el diálogo nativo de Windows. |
| **`StringList`** | Lista multilínea editable con adición, eliminación y reordenación. |
| **`Group`** | Agrupación en tarjeta plegable con campos secundarios (`SubFields`). |
| **`CustomControl`** | Inserta directamente un control WPF `UIElement` personalizado. |

`PluginConfigSchema` admite delegados de ciclo de vida `OnSave` y `OnRollback` para gestionar la persistencia y la restauración personalizada.
