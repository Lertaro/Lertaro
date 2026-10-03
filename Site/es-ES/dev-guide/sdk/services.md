# Servicios del anfitrión

El espacio de nombres `Lertaro.PluginSdk.Services` proporciona servicios estáticos de alto rendimiento que exponen algoritmos, cachés e integraciones de la aplicación anfitriona para su reutilización directa.

## 1. Resumen de servicios estáticos principales

| Servicio del anfitrión | Firmas principales | Capacidades |
| :--- | :--- | :--- |
| **`FuzzyMatchService`** | `bool IsMatch(string pattern, string text)`<br>`bool[]? GetHighlightMask(string text, string query)`<br>`double GetMatchScore(string text, string query)` | Ejecuta el motor de coincidencia difusa fzf del anfitrión, calcula máscaras de resaltado a nivel de carácter y expone la puntuación de calidad de coincidencia para ordenar resultados de forma coherente. |
| **`TranslationService`** | `string Get(string key)`<br>`bool TryGet(string key, out string result)`<br>`string Format(string key, params object[] args)`<br>`string GetCurrentCulture()`<br>`IReadOnlyList<string> GetSupportedCultures(Assembly assembly)`<br>`Dictionary<string, string> LoadEmbeddedTranslations(Assembly assembly, string cultureKey, string typeName)`<br>`event Action<string>? CultureChanged` | Localización dinámica y difusión de cambios de idioma en tiempo de ejecución. `GetCurrentCulture()` devuelve el código de idioma configurado en Configuración (p. ej. `"es-ES"`), independientemente del SO; suscríbase a `CultureChanged` para recargar diccionarios o actualizar el estado cuando el usuario cambie el idioma. `TryGet` responde si una clave se resolvió, y `Get` recurre a un marcador visible `[key]` en lugar de lanzar una excepción. `GetSupportedCultures(assembly)` enumera los idiomas que cubren los recursos incrustados de ese ensamblado; `LoadEmbeddedTranslations(assembly, cultureKey, typeName)` devuelve el diccionario de un solo idioma. |
| **`IconService`** | `ImageSource? GetIcon(string path, bool isDir)`<br>`ImageSource? GetThumbnail(string path, int size)`<br>`ImageSource? GetIconFromCacheOnly(string path, bool isDir, out bool needsLoad)` | Extracción de iconos y miniaturas del Shell de Windows con caché en memoria y disco integrada. `GetIconFromCacheOnly` nunca toca el Shell: devuelve lo que ya está en caché e informa mediante `needsLoad` si aún hace falta una carga real, que es como una lista puede pintarse primero y rellenar los iconos después. |
| **`FavoritesService`** | `IEnumerable<FavoriteItem> GetFavorites()`<br>`bool IsFavorite(string path)`<br>`bool TryAddFavorite(FavoriteItem favorite)` | Lee los favoritos, comprueba si una ruta ya está registrada y añade elementos mediante el puente del anfitrión. |
| **`HistoryService`** | `IEnumerable<HistoryEntry> GetHistoryEntries()` | Consulta el historial de aperturas ordenado por uso reciente, incluyendo términos de búsqueda, tipos y contadores de uso por entrada. Cada ruta física aparece como máximo una vez bajo la palabra clave con la que se abrió más recientemente. |
| **`FileMetadataService`** | `Task<IReadOnlyDictionary<string, FileMetadata>> GetMetadataAsync(IReadOnlyList<string> paths)` | Consulta masiva de tamaños y marcas de tiempo de archivos externos al conjunto activo. |
| **`DirectoryIndexerService`** | `void RegisterDirectory(string pluginId, string directoryPath, bool recursive = true, string filterPattern = "*")`<br>`void UnregisterDirectories(string pluginId)`<br>`IDisposable WatchDirectories(string pluginId, Action onChanged)`<br>`IDisposable WatchDirectories(string pluginId, Action<IReadOnlyList<string>> onChanged)`<br>`void NotifyDirectoryChanged(string pluginId)`<br>`void NotifyDirectoryChanged(string pluginId, IReadOnlyList<string> changedDirectories)`<br>`Task<List<ISearchResult>> SearchDirectoriesAsync(string pluginId, string query, CancellationToken token = default)`<br>`IAsyncEnumerable<ISearchResult> EnumerateDirectoryAsync(string directoryPath, bool recursive = false, string filterPattern = "*", int limit = 0, CancellationToken token = default)` | Registra carpetas para las búsquedas indexadas y el seguimiento de cambios del anfitrión. La enumeración solo lee el índice de archivos del anfitrión y devuelve los resultados en streaming; una carpeta no cubierta produce una secuencia vacía, por lo que debe estar cubierta por un índice configurado de unidad local, red o carpeta. El anfitrión no realiza un análisis directo del sistema de archivos. Nótese la asimetría deliberada: `RegisterDirectory` es recursivo por defecto mientras que `EnumerateDirectoryAsync` no. Las notificaciones se agrupan y pueden incluir los directorios afectados; una lista vacía indica que no se pudo determinar un alcance más preciso. |
| **`MemoryMaintenanceService`** | `void RequestTrim()` | Solicita al anfitrión una limpieza diferida del conjunto de trabajo después de una ráfaga de trabajo en segundo plano con asignaciones temporales. La solicitud puede combinarse o ignorarse y no libera cachés aún activos. |
| **`RecentFilesService`** | `Task<IReadOnlyList<ISearchResult>> GetRecentFilesAsync(IReadOnlyList<string> directories, int limit, int maxAgeMinutes, CancellationToken cancellationToken = default)` | Agrupa los archivos recientes de las carpetas configuradas consultando el índice en memoria del anfitrión. |
| **`ExplorerPathService`** | `string? GetLastActivePath()`<br>`IReadOnlyList<string> GetOpenedFolderPaths()` | Obtiene la última carpeta activa explorada en el Explorador o en cualquier diálogo de archivos, y las carpetas que tiene abiertas ahora mismo. |
| **`PluginSettingsService`** | `T GetSetting<T>(string pluginId, string key, T defaultValue)`<br>`void SetSetting(string pluginId, string key, object? value)`<br>`bool IsComponentEnabled(string dllName, string componentType, string componentName)`<br>`void NotifySettingChanged(string pluginId, string key, object? value = null)`<br>`event Action<string, string>? SettingChanged`<br>`event Action<string, string, object?>? SettingChangedWithValue`<br>`event Action? ComponentEnablementChanged` | Lee y escribe la configuración persistente del plugin y el estado de activación por componente que guarda el anfitrión. `SettingChanged` nombra qué cambió; `SettingChangedWithValue` transporta además el nuevo valor, así que un suscriptor no tiene que volver a leerlo. |
| **`SettingsSearchService`** | `IReadOnlyList<SettingsSearchEntryInfo> GetEntries()`<br>`void Invalidate()` | Lee las opciones de configuración que el anfitrión expone actualmente para búsquedas y permite al anfitrión actualizar su instantánea en caché cuando cambian las entradas dinámicas. |
| **`SettingsWindowService`** | `bool ShowWindow(string? targetSection = null)`<br>`bool ShowEntry(SettingsSearchEntryInfo? entry)` | Solicita al anfitrión mostrar su ventana de Configuración con tema o navegar directamente a una opción, sin iniciar una URI ni otro proceso. |
| **`SearchRefreshService`** | `void RefreshIfMatches(Func<string, bool> queryMatches)` | Notifica al anfitrión para reevaluar búsquedas activas tras completar operaciones asíncronas en segundo plano. |
| **`UserDataService`** | `string? GetUserDataDirectory()`<br>`string? GetSharedDataDirectory()` | Devuelve la carpeta de datos privada del usuario y la carpeta compartida global del equipo. Ambas son anulables: puede que el anfitrión no logre resolver alguna de esas carpetas, así que comprueba `null` en lugar de dar por hecha una ruta. Solo el servicio escribe en la carpeta compartida; los plugins pueden leerla pero deben guardar sus archivos en la carpeta del usuario. |
| **`Logger`** | `void Log(string message, LogLevel level = LogLevel.Info)` | Registra eventos en `app.log`, visibles en tiempo real en el visor de registros de Configuración. Vive en el espacio de nombres raíz `Lertaro.PluginSdk`, **no** en `Lertaro.PluginSdk.Services`. |
| **`PluginPromptService`** | `IReadOnlyDictionary<string, object?>? Prompt(string title, IReadOnlyList<PluginConfigField> fields, IReadOnlyDictionary<string, object?>? initialValues = null)` | Muestra un diálogo modal ligero generado directamente a partir del esquema de campos. Es sincrónico: devuelve los valores enviados, o `null` si el usuario canceló. No lo esperes con `await`. |
| **`PluginNotificationService`** | `INotificationHandle Show(NotificationRequest request)`<br>`Task<NotificationResult> ShowAsync(NotificationRequest request)`<br>`bool Show(string title, string text, Action? onClick = null)` | Muestra una notificación en segundo plano en las propias ventanas del anfitrión: una pila de tarjetas abajo a la derecha (`NotificationPosition.CardStack`) o una línea de aviso centrada en la parte inferior (`BottomNotice`). El anfitrión ajusta la duración al rango que esa posición permite y firma el remitente a partir del ensamblado que llama, así que un plugin no puede falsificar su propia autoría; mientras una aplicación a pantalla completa exclusiva ocupa la pantalla, la tarjeta se reduce a la línea de aviso. No devuelve excepciones al hilo de segundo plano del plugin y la tarea del identificador siempre termina, incluso si nada llegó a la pantalla; la sobrecarga `bool` solo informa de si un anfitrión atendió la petición. Para recibir una respuesta usa `PluginMessageBoxService`. El contrato completo —duraciones y límites, reemplazo por `Id`, motivos de fallo, semántica del clic, colocación e hilos— está en [**Notificaciones**](./notifications). |
| **`PluginMessageBoxService`** | `MessageBoxResult Show(string messageBoxText, string caption = "", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.OK)` | Solicita un cuadro de mensaje gestionado por el anfitrión para que los plugins usen la interfaz temática del anfitrión; utiliza el cuadro del sistema si no hay un controlador registrado. |
| **`ExplorerService`** | `void OpenDirectory(string directoryPath, string? fileNameOrFilePath = null)`<br>`void OpenFolder(string? folderPath)` | Abre el directorio especificado o localiza un archivo, respetando el administrador de archivos de terceros configurado por el anfitrión (o pestañas del Explorador), con respaldo al Explorador del sistema. `OpenFolder` es la forma simple de "abre esta carpeta, o da el foco al Explorador si no hay nada que abrir" y acepta `null`. |

Los índices devueltos por `SettingsSearchService.GetEntries()` solo son válidos durante el proceso actual del anfitrión. Pasa una entrada directamente a `SettingsWindowService.ShowEntry(...)`: el SDK invoca el callback del anfitrión y no construye ni inicia URI `lertaro://`.

`HistoryEntry` expone `Keyword`, `Path`, `Kind`, `Time` (segundos Unix) y `Count` (número de veces que se abrió el elemento). `HistoryService.GetHistoryEntries()` devuelve las entradas en orden de apertura más reciente.

### Activación de componentes y estado de ejecución costoso

`PluginSettingsService.IsComponentEnabled(...)` lee el interruptor por componente administrado por el anfitrión. Los componentes que poseen observadores de directorios, trabajadores en segundo plano, runtimes externos u otro estado costoso deben comprobarlo antes de inicializar ese estado y suscribirse a `ComponentEnablementChanged` para iniciar o detener el runtime correspondiente cuando el usuario cambie el interruptor. El método devuelve `true` si no hay un callback del anfitrión registrado o si este falla, de modo que los plugins sigan funcionando fuera del anfitrión completo.

## 2. Operaciones de archivos nativas del Shell

`Lertaro.PluginSdk.Shell.FileOperations` envuelve la interfaz COM `IFileOperation` de Windows, ofreciendo diálogos de progreso nativos, avisos de conflicto y soporte para deshacer con `Ctrl+Z`:

```csharp
namespace Lertaro.PluginSdk.Shell.FileOperations;

// Pegado o movimiento masivo atómico como una única operación Shell. `move` no tiene valor por
// defecto: especifica cuál quieres.
public static class ShellPasteHelper
{
    public static void PasteAsync(
        IReadOnlyList<string> sourcePaths,
        string destinationFolder,
        bool move,
        Action? onCompleted = null);
}

// Eliminación a la papelera o definitiva. `permanent` tampoco tiene valor por defecto.
public static class ShellDeleteHelper
{
    public static void DeleteAsync(IReadOnlyList<string> paths, bool permanent);
}

// Cambiar el nombre de un único archivo o carpeta existente
public static class ShellRenameHelper
{
    public static void RenameAsync(string path, string newName);
}

// Extracción de archivos virtuales a partir de flujos arrastrados
public static class VirtualFileExtractor
{
    public static bool HasVirtualFiles(IDataObject? data);                        // ¿está presente FileGroupDescriptorW
    public static List<string> Extract(IDataObject? data, string targetFolder);   // sincrónico; devuelve los archivos escritos
    public static string? ResolveDestination(string targetFolder, string name);   // null con entrada vacía; renombra a (2) en conflictos
}
```

Los tres ayudantes de operaciones son **`void` de tipo fire-and-forget**, no `Task`: `PasteAsync` acepta una devolución de llamada `onCompleted` opcional, y los demás no informan de nada. Un conjunto de argumentos vacío o en blanco se ignora en lugar de lanzar una excepción.

> [!TIP]
> Estos ayudantes se mueven por sí solos a un hilo de trabajo STA que **el propio SDK** posee y arranca dentro de tu proceso (`ShellOperationStaWorker`, interno del SDK, no algo que tú registres ni configures), así que un plugin nunca tiene que gestionar los modelos de apartamentos COM para una operación de Shell. La cancelación, y un resultado que esperar, se han omitido a propósito: los diálogos nativos de confirmación y de progreso son la interacción, y un plugin que necesite conocer el resultado debería leer el destino después.

## 3. Ciclo de vida de la aplicación y ventanas de plugins con tema

`AppLifecycleService.RequestRestart()` solicita al anfitrión un reinicio ordenado. El anfitrión inicia el proceso de reemplazo, espera a que la instancia actual complete su cierre normal y después termina; los plugins no necesitan iniciar el ejecutable ni cerrar el anfitrión por su cuenta. El método devuelve `true` cuando el anfitrión acepta la solicitud.

Para el contenido WPF propio de un plugin, `Lertaro.PluginSdk.Windows.PluginWindow` proporciona un marco de ventana redondeado y adaptado al tema del anfitrión. Asigna la vista del plugin a `ContentHostControl.Content` y añade botones inferiores mediante `Footer`. Usa `PluginWindowMode.Window` para una ventana normal en la barra de tareas o `PluginWindowMode.Dialog` para un diálogo siempre visible y oculto en Alt+Tab. Si no se especifica un icono, se usa el icono de aplicación predeterminado del anfitrión.

```csharp
var window = new PluginWindow("Herramienta", 720, 470, PluginWindowMode.Dialog);
window.ContentHostControl.Content = new MyView();
window.Footer.Children.Add(new Button { Content = "Aceptar", IsDefault = true });
window.ShowDialog();
```

`PluginWindow.ShowFooter` desactiva la fila inferior cuando una herramienta no tiene botones; si se omite el argumento del icono, se recurre al icono de aplicación predeterminado del anfitrión.

## 4. Infraestructura de ventanas, consultas, temas y vista previa

| Servicio del anfitrión | Firmas principales | Capacidades |
| :--- | :--- | :--- |
| **`SearchWindowService`** | `bool IsWindowVisible()`<br>`void ShowWindow(string? query = null)`<br>`void HideWindow()`<br>`void FocusQueryTextBox()` | Consulta y maneja las ventanas de búsqueda del anfitrión desde un plugin, incluso entregándole una consulta inicial. |
| **`SearchQueryService`** | `void ChangeQuery(string query, bool requery = false)`<br>`string StripQueryTokens(string query)` | Escribe la consulta de la ventana activa, opcionalmente volviendo a ejecutar la búsqueda, y recorta los tokens de sufijo del anfitrión para que un plugin vea el texto plano que escribió el usuario. |
| **`ThemeService`** | `bool IsDarkTheme` | Una sola marca para un plugin que pinta su propio contenido. Cuando no hay delegado del anfitrión, lee los recursos de la aplicación WPF en ejecución, así que un plugin renderizado fuera del lanzador también recibe una respuesta en lugar de una excepción. |
| **`PluginPreviewCache`** | `string Register(string title, string pluginName, Lazy<UserControl> factory, Func<object?>? iconProvider = null)`<br>`PluginPreviewEntry? GetEntry(string key)`<br>`UIElement? GetPreview(string key)` | Registra de forma perezosa un control de vista previa propiedad del plugin y entrega al anfitrión la clave para buscarlo, de modo que el control se construye la primera vez que realmente se muestra y no al cargarse. |
| **`PreviewActivationSignal`** | `void Begin()`<br>`void End()`<br>`bool IsActive`<br>`event Action? FocusStolen` / `void NotifyFocusStolen()` | Queda establecido mientras un manejador nativo **externo al proceso** esté alojado de forma activa —toda la sesión de visualización, no solo su arranque en frío, porque interactuar con su contenido renderizado puede abrir una ventana de nivel real—. Se cuenta una profundidad anidada de `Begin`/`End`, así que los proveedores deben emparejar sus llamadas. *Comportamiento actual del anfitrión, no un contrato:* mientras la señal está establecida, las ventanas de búsqueda dejan de tratar su propia desactivación como «el usuario hizo clic fuera». |
| **`PreviewDialogSignal`** | `void NotifyDialogOpened()`<br>`void NotifyDialogClosed()`<br>`event Action? DialogOpened` / `DialogClosed` | Emitida por un proveedor cuando aparece la ventana emergente propia de un manejador nativo (la petición de Word para "escribir la contraseña" de un archivo cifrado que se está previsualizando es el caso para el que existe). *Comportamiento actual del anfitrión, no un contrato:* la ventana rápida y su vista previa se ocultan mientras el diálogo esté abierto, para que pueda alcanzarse, y después se restauran. |
| **`LocalSendTransferService`** | `void OpenSendWindow(IReadOnlyList<string>? files, string? text)` | Abre la ventana LocalSend del anfitrión precargada con una lista de archivos o un texto, de modo que un plugin puede entregar una transferencia sin poseer ninguna parte de la interfaz. |
| **`ToolRunService`** | `Func<string, string, Task<string?>>? RunDopusPathsFunc` (asignado por el anfitrión) | Ejecuta una herramienta externa en el proceso que de verdad puede responderle. El gancho de teclado del anfitrión está elevado, y un `dopusrt.exe` elevado nunca puede ser respondido por un Directory Opus sin elevación (UIPI bloquea la respuesta), mientras que degradar un proceso requiere un privilegio que el token del gancho no posee. La App se ejecuta al nivel del propio usuario, así que el gancho reenvía la petición allí. Lee el delegado y trata `null` como indisponible; el llamador crea primero el archivo de salida, porque la herramienta rellena un archivo que ya existe. |
