# Arquitectura del sistema

Lertaro está construido sobre un modelo de aislamiento multiproceso y una arquitectura modular por capas, garantizando búsquedas en submilisegundos y una integración de escritorio con seguridad y estabilidad.

![Diagrama de arquitectura de Lertaro](/architecture.svg)

## 1. Modelo de aislamiento de tres procesos

Para evitar que el fallo de un único componente provoque el cierre del sistema y para limitar los privilegios elevados al mínimo indispensable, la ejecución se divide en tres procesos independientes:

### 1. Servicio de indexación en segundo plano (`Lertaro.Service.exe`)

- **Identidad**: Se ejecuta de forma continua como un servicio de Windows con la cuenta `LocalSystem`.
- **Responsabilidades**: Indexación del sistema de archivos y seguimiento de cambios. Lee los diarios USN y las tablas \$MFT de volúmenes NTFS / ReFS; monitoriza eventos de cambio en FAT32 / exFAT; escanea y almacena en caché recursos compartidos SMB / NAS.
- **Seguridad y rendimiento**: Al ejecutarse en el nivel SYSTEM, accede a los metadatos de los volúmenes sin mostrar cuadros de UAC, respondiendo a la aplicación en modo usuario mediante tuberías con nombre de alta velocidad sin otorgar privilegios innecesarios a la interfaz.

### 2. Aplicación de interacción con el usuario (`Lertaro.App`)

- **Identidad**: Aplicación de escritorio WPF estándar en modo usuario aislada por sesión.
- **Responsabilidades**: Aloja la barra de búsqueda rápida, la ventana principal, el Centro de configuración, la gestión de atajos globales, el menú de acciones (`Ctrl+O`) y las vistas previas de QuickLook.
- **Puente IPC y alojamiento CLI**: Habla con el servicio en segundo plano a través de la tubería con nombre bidireccional `LertaroPipe` (`Core.Services.Search.SearchService` es el cliente del lado de la App). También aloja una tubería dedicada por usuario (`AppSearchPipeService`), lo que permite a la utilidad `lff` reutilizar las tablas de memoria y plugins de la aplicación sin reinicializaciones independientes.

### 3. Proceso de interceptación de teclado y adaptadores (`Lertaro.Service --hook`)

- **Identidad**: Proceso auxiliar iniciado por el servicio en segundo plano. Se lanza **elevado solo cuando la cuenta con la que se inició sesión es verdaderamente administradora**; en caso contrario se ejecuta con el token del propio usuario, así que la omisión descrita abajo solo está disponible en esa máquina.
- **Responsabilidades**: Aloja los enlaces de teclado de bajo nivel y la escucha global de eventos de ratón.
- **Omisión de UIPI y aislamiento de fallos**: El aislamiento de privilegios de interfaz (UIPI) de Windows impide que procesos de menor integridad envíen mensajes a ventanas elevadas. Al ejecutar los adaptadores de ventana ([`IActivePathCollector`, `IFileDialogAdapter`, `IInlineSearchAdapter`](./sdk/system-adapters)) dentro de este proceso, Lertaro puede leer y manejar instancias del Explorador y diálogos de archivos ejecutados como Administrador cuando el enlace se lanzó elevado. Los fallos en los enlaces de teclado tampoco afectan a la aplicación principal.

## 2. Librería central compartida (`Lertaro.Core`)

`Lertaro.Core` es referenciada conjuntamente por los tres procesos, e incluye:

- **Motor de coincidencia difusa fzf (`Core/SearchIndex/Fzf/*`)**: Coincidencia de caracteres dispersos, puntuación y cálculo de máscaras de resaltado optimizados, junto con `SearchQueryParser` para letras de unidad y patrones de ruta.
- **Índice columnar en memoria (`Core/IndexV2/*`)**: Instantáneas columnares proyectadas en memoria combinadas con una capa de deltas en memoria para búsquedas instantáneas entre cientos de millones de archivos.
- **Protocolos binarios IPC**: Estructuras de mensajes serializadas (`SearchRequestMessage`) para transferencias interproceso sin copias redundantes.
- **Sistema de registro unificado (`Logger`)**: Emite registros hacia `service.log`, `app.log` y `hook.log`, accesibles de forma centralizada en el visor de registros.

## 3. Posición del sistema de plugins en la arquitectura

Todos los plugins se compilan contra `Lertaro.PluginSdk` y se cargan dinámicamente al iniciar `Lertaro.App`:

- **Comunicación sin privilegios**: El código del propio plugin se ejecuta en el proceso de quien lo cargó —normalmente la App, aunque la siguiente viñeta nombra los dos tipos que el Servicio también carga—. Nada cruza un límite de proceso por sí mismo, así que un plugin que necesite indexar carpetas personalizadas delega el trabajo en `DirectoryIndexerService` en lugar de recorrer el disco él mismo.
- **Doble carga selectiva**: Las fuentes de búsqueda, las acciones y los componentes de interfaz se ejecutan solo en el proceso App. Tres cosas se cargan además en los otros procesos: los adaptadores de ventanas y de diálogos de archivos (`IActivePathCollector`, `IFileDialogAdapter`, `IInlineSearchAdapter`) van al proceso Hook para la automatización de ventanas entre niveles de integridad, y `IAliasProvider` y `ITranslationProvider` los carga también el **Servicio** (`ServicePluginLoader`), porque el indexador construye filas de alias y necesita la misma transliteración y los mismos nombres que muestra la interfaz.
