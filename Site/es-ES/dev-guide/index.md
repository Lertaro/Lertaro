# Guía de desarrollo

Bienvenido al Manual de referencia para desarrolladores de Lertaro. Diseñado sobre una arquitectura desacoplada y un ecosistema de plugins extensible, Lertaro proporciona un SDK oficial: `Lertaro.PluginSdk`. Al utilizar este SDK, los desarrolladores pueden aportar fuentes de búsqueda personalizadas, ampliar menús de acciones contextuales, integrarse con exploradores de archivos y cuadros de diálogo nativos, y crear temas o visores de vista previa a medida.

## 1. Arquitectura y flujo de trabajo

- **[Arquitectura del sistema](./architecture)** —— Explicación del modelo de aislamiento de tres procesos (Servicio de Windows a nivel SYSTEM, App WPF en modo usuario y proceso de interceptación de teclado Hook) y la comunicación IPC por tuberías con nombre.
- **[Guía de inicio rápido](./getting-started)** —— Guía paso a paso para crear un proyecto de librería, referenciar el SDK, implementar `IPlugin` y depurar localmente.
- **[Empaquetado y distribución](./packaging)** —— Estructura de carpetas de plugins, inclusión de librerías dependientes administradas y nativas, recursos i18n incrustados y automatización PostBuild.
- **[Ejemplos de plugins](./examples)** —— Análisis en profundidad del código de los plugins oficiales de código abierto `CoreExtensions`, `PinyinAlias` y `FlowLauncherBridge`.

## 2. Referencia de la API del SDK

| Categoría del SDK | Interfaces y servicios principales | Descripción de capacidades |
| :--- | :--- | :--- |
| **[Búsqueda central y acciones](./sdk/core-search-actions)** | `ISearchableItemProvider`<br>`IInstantResultProvider`<br>`IFullSearchFileResultProvider`<br>`IAliasProvider`<br>`IQueryTokenProvider`<br>`ISearchResultAction`<br>`IDynamicActionProvider` | Aportar elementos indexados, respuestas de cálculo instantáneo, resultados de archivos para la Ventana principal, motores de transliteración de alias no ASCII, manejadores de tokens de sufijo y menús de acciones estáticos o dinámicos. |
| **[Adaptadores de sistema y diálogo](./sdk/system-adapters)** | `IActivePathCollector`<br>`IFileDialogAdapter`<br>`IInlineSearchAdapter`<br>`IQuickNavigationProvider` | Detectar directorios activos en exploradores, enganchar diálogos nativos de archivos, incrustar la barra de búsqueda con sincronización bidireccional y menús de Navegación rápida. |
| **[Extensiones de interfaz y vista previa](./sdk/ui-extensions)** | `ISidebarFilterProvider`<br>`IResultColumnProvider`<br>`IQuickPanelTabProvider`<br>`IFilePreviewProvider`<br>`IThumbnailProvider`<br>`IThemeProvider`<br>`ITranslationProvider` | Categorías de filtro lateral, columnas personalizadas en tablas, pestañas dinámicas en el Panel rápido, renderizadores de vista previa en QuickLook, extracción de miniaturas, temas WPF e idiomas i18n. |
| **[Abstracciones compartidas](./sdk/abstractions)** | `ISearchResult`<br>`FileMetadata`<br>`IPluginSearchWindow`<br>`IConfigurable` | Modelos de resultados de solo lectura, metadatos de archivos de alta precisión, controladores seguros de la ventana anfitriona y formularios de configuración basados en esquemas. |
| **[Servicios del anfitrión](./sdk/services)** | `FuzzyMatchService`<br>`TranslationService`<br>`IconService`<br>`FavoritesService`<br>`HistoryService`<br>`FileMetadataService`<br>`DirectoryIndexerService`<br>`RecentFilesService`<br>`ExplorerPathService`<br>`PluginSettingsService`<br>`SettingsSearchService`<br>`SettingsWindowService`<br>`SearchRefreshService`<br>`UserDataService`<br>`Logger` | Infraestructura de alto rendimiento: coincidencia difusa fzf y máscaras de resaltado, extracción de iconos con caché, gestión de favoritos y consulta del historial, proxies de indexación, aislamiento de datos y operaciones Shell. |

> [!NOTE]
> Todas las firmas de métodos, parámetros y contratos de comportamiento de este manual han sido contrastados directamente con el código fuente de `Lertaro.PluginSdk`.
