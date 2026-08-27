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
