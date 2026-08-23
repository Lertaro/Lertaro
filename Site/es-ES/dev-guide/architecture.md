# Arquitectura

![Lertaro architecture](/architecture.svg)

## División en procesos

Lertaro se ejecuta como tres procesos independientes, deliberadamente aislados por nivel de privilegio y ciclo de vida:

- **`Lertaro.Service`** — un servicio de Windows que se ejecuta como `LocalSystem`. Es responsable de toda la indexación de archivos:
  lee el USN Journal y la MFT de las unidades NTFS/ReFS, recorre y vigila directamente otros sistemas de archivos locales (que no
  tienen journal que leer), escanea y almacena en caché los recursos compartidos de red, y
  responde a las consultas de búsqueda a través de una named pipe. Ejecutar esto a nivel SYSTEM permite leer los
  metadatos de volumen sin procesar que cualquier cuenta de usuario tiene permiso de ver, sin conceder al proceso interactivo de la App
  privilegios elevados que no necesita.
- **`Lertaro.App`** — la aplicación WPF de sesión, por usuario: las ventanas de búsqueda, la
  ventana de Configuración, la gestión de atajos y la interfaz de Acciones/QuickLook. Se comunica con el Servicio mediante una
  named pipe (`SearchService`/`UsnServicePipeServer` en `Core.Services`) y nunca accede directamente al índice
  en disco. También aloja una segunda pipe propia, por usuario (`AppSearchPipeService`), que
  permite a la utilidad de línea de comandos `lff` (ver [Búsqueda por línea de comandos](../user-guide/cli)) reutilizar el estado de búsqueda
  ya inicializado de la App — proveedores de alias/plugins cargados, índices de unidades de red configurados —
  en lugar de que un proceso cliente independiente tenga que replicar esa configuración por sí mismo.
- **`Lertaro.Service --hook`** — un pequeño proceso independiente que aloja el hook de teclado global de bajo nivel,
  de modo que un fallo del hook o una aplicación en primer plano mal comportada no puedan hacer caer con ellos al proceso principal de la App.
  También carga los adaptadores de integración de ventana de los plugins y ejecuta sus llamadas él mismo — ver
  [Dónde encajan los plugins](#donde-encajan-los-plugins) más abajo.

## Núcleo compartido

`Core` es una biblioteca de clases referenciada tanto por el Servicio como por la App. Contiene:

- El motor de búsqueda (`Core/SearchIndex/Fzf/*`) — una implementación de coincidencia difusa basada en el
  algoritmo de la herramienta de línea de comandos `fzf`, más un analizador de consultas (`SearchQueryParser`) para segmentar
  por letra de unidad y para el modo de búsqueda por ruta.
- El índice en tiempo de ejecución (`Core/IndexV2/*`) — un formato de instantánea columnar mapeado en memoria, construido a partir de
  las lecturas de USN/MFT, con una capa delta en memoria para los cambios posteriores a la última instantánea.
- Contratos de IPC (`SearchRequestMessage`, `SearchResponseBinarySerializer`, ...) compartidos literalmente por
  ambos procesos, de modo que la App y el Servicio siempre coinciden en el formato de la comunicación.
- `Logger` — escribe en archivos de registro por proceso (`service.log`, `app.log`, `hook.log`), todos legibles
  (aunque no todos editables) desde el visor de registros de la App en Configuración → Estado del Servicio.

## Dónde encajan los plugins

Los plugins son ensamblados `.dll` que referencian `PluginSdk` y son cargados por el proceso de la App (ver
[Primeros pasos](./getting-started) y [Empaquetado y despliegue](./packaging)). Lertaro incluye
plugins integrados como ejemplos de referencia — `Lertaro.Plugins.CoreExtensions` (acciones de archivo integradas
y la integración con el menú contextual del shell), `Lertaro.Plugins.PinyinAlias` (alias en pinyin
para nombres de archivo en chino) y `Lertaro.Plugins.FlowLauncherBridge` (que conecta plugins de Flow Launcher
de terceros en formatos C#, Python 3.12, Node.js v20 LTS y ejecutables con entornos aislados) — ver [Plugins de ejemplo](./examples) para un recorrido por los plugins nativos.

Los plugins nunca se comunican directamente con el Servicio; interactúan con la App a través de las interfaces
documentadas en la Referencia del SDK de Plugins, y con el índice en disco (cuando necesitan directorios indexados
personalizados) a través de `DirectoryIndexerService`, que actúa de intermediario con el Servicio en su nombre.

Los adaptadores de integración de ventana son la única excepción a ser exclusivos de la App:
las implementaciones de [`IActivePathCollector`, `IFileDialogAdapter` e `IInlineSearchAdapter`](./sdk/system-adapters)
se cargan una segunda vez en el proceso Hook, y sus llamadas se ejecutan ahí en lugar de en la App. Esto es lo que permite
que Lertaro controle una ventana elevada del Explorador de archivos, un diálogo de archivos o un gestor de archivos de terceros,
aunque la propia App siempre se ejecute sin elevación — Windows impide que un proceso con menos privilegios
envíe entradas a uno con más privilegios, por lo que la llamada debe originarse
en un proceso que se ejecute con el mismo nivel de privilegio que la ventana de destino.
