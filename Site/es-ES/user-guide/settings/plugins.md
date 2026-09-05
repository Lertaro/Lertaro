# Plugins (Gestión)

Lertaro cuenta con una arquitectura modular de extensiones. Tanto los componentes internos como los plugins nativos en C# y el ecosistema de Flow Launcher se gestionan en **Configuración → Plugins**.

## 1. Diseño de página y versión del SDK

- **Insignia del SDK**: En la esquina superior derecha se muestra la versión cargada de `Lertaro.PluginSdk`. Al hacer clic se abre la [**Guía de desarrollo**](../../dev-guide/).
- **Diseño de doble panel independiente**: La columna izquierda muestra los plugins instalados y la derecha presenta los detalles y el formulario de configuración del plugin seleccionado, con desplazamiento independiente.

## 2. Detalles del plugin y conmutadores de componentes

Al seleccionar un plugin a la izquierda, el panel derecho muestra su icono, nombre, versión y descripción:

### Pestaña Detalles (Details)

- **Lista de componentes**: Muestra todos los componentes funcionales registrados (fuentes de búsqueda, proveedores de acciones, accesos rápidos, manejadores de vista previa, etc.).
- **Conmutadores individuales**: Cada componente no esencial dispone de una **casilla de activación**; los componentes imprescindibles muestran un icono de candado y no se pueden desactivar.
- **Seleccionar / Deseleccionar todo**: Enlace rápido para alternar en bloque todos los elementos de un grupo.
- **Descripción emergente**: Pasa el ratón sobre el icono **(!)** de un componente para consultar sus detalles técnicos y activación.

### Pestaña Configuración (Configure)

- **Formularios incrustados**: Las opciones personalizadas del plugin se muestran en el panel derecho sin cuadros de diálogo adicionales (campos de texto, números, selectores, pestañas, etc.).
- **Campos de icono**: Los campos de texto con la clave de esquema `Icon` muestran una vista previa y admiten WPF Path Data. Al pegar un documento SVG/XML completo, sus datos de ruta se extraen y combinan automáticamente; el contenido no válido se borra y se notifica mediante un cuadro de diálogo con el tema de Lertaro.
- **Guardado seguro y restauración**: Los cambios se conservan en memoria hasta pulsar **Aceptar**; cambiar de plugin o salir de la página restaura los valores anteriores automáticamente.

## 3. Soporte del ecosistema de plugins de Flow Launcher

Además de los plugins nativos de `Lertaro.PluginSdk`, el módulo integrado **Flow Launcher Bridge** ofrece compatibilidad total con el extenso catálogo de Flow Launcher.

### Entornos aislados y multilingües

- **Compatibilidad total**: Ejecuta plugins de Flow Launcher escritos en **C# (.NET)**, **Python 3.12**, **Node.js v20 LTS** y binarios ejecutables (`.exe`).
- **Aislamiento sin alterar el sistema**: Los entornos de Python (`FlowData\PythonEmbeded-{arch}`) y Node.js (`FlowData\NodeEmbeded-{arch}`) se despliegan automáticamente dentro de la carpeta de datos de Lertaro sin modificar la variable PATH de Windows.
- **Instalación automática de dependencias**: Al cargar un plugin por primera vez, instala silenciosamente las librerías necesarias mediante `requirements.txt` (Python pip) o `package.json` (Node.js npm).

### Gestión desde la barra de búsqueda

Gestiona plugins de Flow directamente con comandos en la barra de búsqueda:

- **`flow install <palabra clave>`**: Busca en el repositorio oficial de Flow.Launcher y descarga, extrae e instala el plugin y sus dependencias en un solo paso.
- **`flow update`**: Comprueba actualizaciones de los plugins instalados y los actualiza.
- **`flow uninstall <nombre>`**: Desinstala el plugin y limpia sus carpetas locales.
- **Instalación manual**: Puedes colocar carpetas de plugins descargados en `<DirectorioUsuario>\FlowData\Plugins\`.

### Configuración, palabras clave y vistas previas enriquecidas

- **Gestión de ActionKeyword**: En **Configuración → Plugins → Flow Launcher Bridge → Configuración**, activa o desactiva plugins individuales y modifica su **ActionKeyword** (almacenado en `FlowData\Settings\Plugins.json`).
- **Formularios dinámicos**: Compatible con plantillas YAML/JSON (`SettingsTemplate.yaml`/`.json`) y paneles WPF (`ISettingProvider`), adaptados al tema visual activo y con soporte de traducción.
- **Vistas previas interactivas con WebView2**: Muestra paneles ricos (diccionarios MDict, pronósticos del tiempo, depuradores API, capturas web) en la ventana de QuickLook.
- **Integración profunda con el anfitrión**: Los plugins de Flow que abren carpetas respetan automáticamente el administrador de archivos de terceros configurado por el anfitrión, los cuadros de mensaje se muestran con el tema visual nativo y los registros internos se integran en el visor de registros de Configuración.
- **Listado rápido**: Escribe `flow` en la barra de búsqueda para ver todos los plugins cargados y sus palabras clave activas; al seleccionar un plugin se abre su grupo de configuración correspondiente.

## 4. Selector de dispositivos de audio

El **Selector de dispositivos de audio** es un plugin nativo exclusivo para Windows. Escribe `ad` para mostrar los dispositivos activos de salida y entrada, y selecciona uno para convertirlo en el dispositivo multimedia predeterminado correspondiente. También puedes añadir el nombre de un dispositivo después de la palabra clave para filtrar los resultados.

En su configuración puedes cambiar la palabra clave activadora y elegir si los resultados muestran el nombre descriptivo, el nombre del dispositivo o su descripción. Cambia el punto final predeterminado de salida o entrada, pero no controla el volumen, el silencio ni el enrutamiento de audio por aplicación.

## 5. Búsqueda de contenido

El plugin **Búsqueda de contenido** busca en el texto de los documentos locales configurados. En la pestaña **Configurar** puedes establecer la palabra clave activadora, las carpetas supervisadas, las extensiones indexadas, el límite por archivo, el límite de tamaño del índice y las expresiones regulares para excluir rutas completas. La palabra clave predeterminada es `cs`; escribe `cs plan del proyecto` para buscar en el contenido y mostrar archivos coincidentes con fragmentos.

La configuración también ofrece **Borrar índice** y **Reconstruir índice**. Borrar elimina solo el índice de contenido, mientras que Reconstruir lo elimina y vuelve a analizar todas las carpetas supervisadas. Ninguna de las dos acciones elimina el índice normal de nombres de archivo de Lertaro.

## 6. Estado de ejecución de plugins

La pestaña **Estado de ejecución**, dentro de **Configuración → Plugins**, muestra la actividad del anfitrión para los plugins instalados mientras Lertaro está en ejecución. Los valores se agregan por plugin y se actualizan mientras la pestaña está abierta.

- **Filtro de plugins**: Usa el cuadro de filtro para buscar de forma difusa por nombre de plugin.
- **Columnas ordenables**: Haz clic en un encabezado para alternar entre orden ascendente, descendente y el orden predeterminado de plugins.
- **Métricas disponibles**: Número de llamadas, duración media, duración más reciente, duración máxima, asignación administrada y excepciones observadas.
- **Alcance de la sesión**: Las estadísticas se acumulan durante el proceso actual de Lertaro y se reinician al volver a iniciar la aplicación. Un cero indica que todavía no se ha registrado una llamada medida. La asignación administrada es la memoria asignada por las llamadas medidas, no el uso total de memoria privada del plugin.
