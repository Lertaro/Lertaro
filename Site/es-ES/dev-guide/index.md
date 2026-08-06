# Manual de Desarrollador

Lertaro ofrece un SDK de plugins abierto (`Lertaro.PluginSdk`) que ensamblados de terceros pueden usar como
destino para ampliar el comportamiento de búsqueda, añadir acciones de menú contextual, integrarse con otras ventanas y
personalizar la interfaz. Este manual documenta esa superficie.

- **[Arquitectura](./architecture)** — cómo encajan entre sí la App, el Servicio en segundo plano y los plugins.
- **[Primeros pasos](./getting-started)** — cómo crear la estructura de un proyecto de plugin y cargarlo.
- **Referencia del SDK de Plugins**:
  - **[Búsqueda y acciones principales](./sdk/core-search-actions)** — cómo contribuir resultados de búsqueda y
    acciones de resultado.
  - **[Adaptadores de sistema y de diálogo](./sdk/system-adapters)** — integración con el Explorador de archivos, diálogos
    de archivo nativos y otras ventanas en primer plano.
  - **[Extensiones de interfaz y vista previa](./sdk/ui-extensions)** — filtros de la barra lateral, columnas de resultado,
    vistas previas de archivo, miniaturas, temas y traducciones.
  - **[Abstracciones compartidas](./sdk/abstractions)** — los modelos de solo lectura que reciben los plugins
    (`ISearchResult`, `IPluginSearchWindow`) y el esquema de configuración (`IConfigurable`).
  - **[Servicios del host](./sdk/services)** — servicios estáticos que el host expone a los plugins
    (iconos, favoritos, historial, metadatos de archivo, indexación de directorios, configuración por plugin, registro).
- **[Plugins de ejemplo](./examples)** — dos plugins reales, ya distribuidos, como casos de estudio.
- **[Empaquetado y despliegue](./packaging)** — cómo se detecta y carga una DLL de plugin ya compilada.

Todas las firmas de interfaz de este documento se han verificado directamente contra el código fuente actual de
`PluginSdk` — si encuentras alguna discrepancia, el código es la fuente autorizada.
