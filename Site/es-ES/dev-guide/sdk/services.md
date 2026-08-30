# Servicios del anfitrión

El espacio de nombres `Lertaro.PluginSdk.Services` proporciona servicios estáticos de alto rendimiento que exponen algoritmos, cachés e integraciones de la aplicación anfitriona para su reutilización directa.

## 1. Resumen de servicios estáticos principales

| Servicio del anfitrión | Firmas principales | Capacidades |
| :--- | :--- | :--- |
| **`FuzzyMatchService`** | `bool IsMatch(string pattern, string text)`<br>`bool[]? GetHighlightMask(string text, string query)`<br>`double GetMatchScore(string text, string query)` | Ejecuta el motor de coincidencia difusa fzf del anfitrión, calcula máscaras de resaltado a nivel de carácter y expone la puntuación de calidad de coincidencia para ordenar resultados de forma coherente. |
| **`TranslationService`** | `string Get(string key)`<br>`string Format(string key, params object[] args)`<br>`void LoadEmbeddedTranslations(...)`<br>`string GetCurrentCulture()`<br>`event Action<string>? CultureChanged` | Localización dinámica y difusión de cambios de idioma en tiempo de ejecución. `GetCurrentCulture()` devuelve el código de idioma configurado en Configuración (p. ej. `"es-ES"`), independientemente del SO; suscríbase a `CultureChanged` para recargar diccionarios o actualizar el estado cuando el usuario cambie el idioma. |
| **`IconService`** | `ImageSource? GetIcon(string path, bool isDir)`<br>`ImageSource? GetThumbnail(string path, int size)` | Extracción de iconos y miniaturas del Shell de Windows con caché en memoria y disco. |
| **`FavoritesService`** | `IReadOnlyList<FavoriteItem> GetFavorites()`<br>`bool IsFavorite(string path)`<br>`bool TryAddFavorite(FavoriteItem favorite)` | Lee los favoritos, comprueba si una ruta ya está registrada y añade elementos mediante el puente del anfitrión. |
| **`HistoryService`** | `IReadOnlyList<HistoryEntry> GetHistoryEntries()` | Consulta el historial de aperturas ordenado por uso reciente, incluyendo términos de búsqueda y tipos. |
| **`FileMetadataService`** | `Task<IReadOnlyDictionary<string, FileMetadata>> GetMetadataAsync(IEnumerable<string> paths)` | Consulta masiva de tamaños y marcas de tiempo de archivos externos al conjunto activo. |
| **`DirectoryIndexerService`** | `void RegisterDirectory(string pluginId, string path, bool recursive, string? filterPattern)`<br>`IDisposable WatchDirectories(string pluginId, Action onChanged)`<br>`IAsyncEnumerable<ISearchResult> EnumerateDirectoryAsync(...)` | Registra carpetas para indexación y seguimiento en segundo plano; permite enumeraciones en streaming sin E/S de disco. |
| **`RecentFilesService`** | `Task<IReadOnlyList<ISearchResult>> GetRecentFilesAsync(IEnumerable<string> directories, int limit, int maxAgeMinutes, CancellationToken token)` | Consulta el índice en memoria para extraer en submilisegundos los archivos modificados recientemente. |
| **`ExplorerPathService`** | `string? GetLastActivePath()` | Obtiene la última carpeta activa explorada en el Explorador o en cualquier diálogo de archivos. |
| **`PluginSettingsService`** | `T GetSetting<T>(string pluginId, string key, T defaultValue)`<br>`event Action<string, string>? SettingChanged` | Lectura de configuraciones del plugin (valor de usuario > valor por defecto de esquema > valor de respaldo). |
| **`SettingsSearchService`** | `IReadOnlyList<SettingsSearchEntryInfo> GetEntries()`<br>`void Invalidate()` | Lee las opciones de configuración que el anfitrión expone actualmente para búsquedas y permite al anfitrión actualizar su instantánea en caché cuando cambian las entradas dinámicas. |
| **`SettingsWindowService`** | `bool ShowWindow(string? targetSection = null)`<br>`bool ShowEntry(SettingsSearchEntryInfo? entry)` | Solicita al anfitrión mostrar su ventana de Configuración con tema o navegar directamente a una opción, sin iniciar una URI ni otro proceso. |
| **`SearchRefreshService`** | `void RefreshIfMatches(Func<string, bool> queryMatches)` | Notifica al anfitrión para reevaluar búsquedas activas tras completar operaciones asíncronas en segundo plano. |
| **`UserDataService`** | `string GetUserDataDirectory()`<br>`string GetSharedDataDirectory()` | Devuelve la carpeta de datos privada del usuario y la carpeta compartida global del equipo (p. ej. runtimes Python/Node). |
| **`Logger`** | `void Log(string message, LogLevel level = LogLevel.Info)` | Registra eventos en `app.log`, visibles en tiempo real en el visor de registros de Configuración. |
| **`PluginPromptService`** | `Task<Dictionary<string, object?>?> Prompt(string title, IEnumerable<PluginConfigField> fields, ...)` | Muestra un diálogo modal ligero generado automáticamente a partir de un esquema de campos. |
| **`PluginMessageBoxService`** | `MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)` | Solicita un cuadro de mensaje gestionado por el anfitrión para que los plugins usen la interfaz temática del anfitrión; utiliza el cuadro del sistema si no hay un controlador registrado. |
| **`ExplorerService`** | `void OpenDirectory(string directoryPath, string? fileNameOrFilePath = null)` | Abre el directorio especificado o localiza un archivo, respetando el administrador de archivos de terceros configurado por el anfitrión (o pestañas del Explorador), con respaldo al Explorador del sistema. |

Los índices devueltos por `SettingsSearchService.GetEntries()` solo son válidos durante el proceso actual del anfitrión. Pasa una entrada directamente a `SettingsWindowService.ShowEntry(...)`: el SDK invoca el callback del anfitrión y no construye ni inicia URI `lertaro://`.

## 2. Operaciones de archivos nativas del Shell

`Lertaro.PluginSdk.Shell.FileOperations` envuelve la interfaz COM `IFileOperation` de Windows, ofreciendo diálogos de progreso nativos, avisos de conflicto y soporte para deshacer con `Ctrl+Z`:

```csharp
namespace Lertaro.PluginSdk.Shell.FileOperations;

// Pegado o movimiento masivo atómico mediante el Shell
public static class ShellPasteHelper
{
    public static void PasteAsync(
        IEnumerable<string> sourcePaths,
        string destinationFolder,
        bool move = false,
        Action? onCompleted = null);
}

// Eliminación a la papelera o definitiva
public static class ShellDeleteHelper
{
    public static void DeleteAsync(IEnumerable<string> paths, bool permanent = false);
}

// Extracción de archivos virtuales a partir de flujos arrastrados
public static class VirtualFileExtractor
{
    public static bool HasVirtualFiles(IDataObject dataObject);
    public static Task<IReadOnlyList<string>> Extract(IDataObject dataObject, string targetFolder);
    public static string ResolveDestination(string folder, string name); // Renombrado automático a (2) en conflictos
}
```

> [!TIP]
> Estos ayudantes del Shell se ejecutan en un subproceso STA dedicado (`ShellOperationStaWorker`), por lo que los plugins no necesitan gestionar manualmente modelos de apartamentos COM.

## 3. Ciclo de vida de la aplicación y ventanas de plugins con tema

`AppLifecycleService.RequestRestart()` solicita al anfitrión un reinicio ordenado. El anfitrión inicia el proceso de reemplazo, espera a que la instancia actual complete su cierre normal y después termina; los plugins no necesitan iniciar el ejecutable ni cerrar el anfitrión por su cuenta. El método devuelve `true` cuando el anfitrión acepta la solicitud.

Para el contenido WPF propio de un plugin, `Lertaro.PluginSdk.Windows.PluginWindow` proporciona un marco de ventana redondeado y adaptado al tema del anfitrión. Asigna la vista del plugin a `ContentHostControl.Content` y añade botones inferiores mediante `Footer`. Usa `PluginWindowMode.Window` para una ventana normal en la barra de tareas o `PluginWindowMode.Dialog` para un diálogo siempre visible y oculto en Alt+Tab. Si no se especifica un icono, se usa el icono de aplicación predeterminado del anfitrión.

```csharp
var window = new PluginWindow("Herramienta", 720, 470, PluginWindowMode.Dialog);
window.ContentHostControl.Content = new MyView();
window.Footer.Children.Add(new Button { Content = "Aceptar", IsDefault = true });
window.ShowDialog();
```
