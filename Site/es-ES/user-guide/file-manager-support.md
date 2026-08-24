# Exploradores de archivos compatibles e integración de diálogos

Lertaro va más allá de ser un lanzador independiente: se integra profundamente en el Explorador de Windows, exploradores de terceros y cuadros de diálogo de software, agilizando enormemente la apertura, guardado y navegación de carpetas.

## 1. Tres capacidades principales de integración

En función de las características de la ventana anfitriona, Lertaro ofrece hasta tres funciones de integración:

- **Búsqueda incrustada (Inline Search)**: La barra de búsqueda rápida de Lertaro se incrusta en la parte superior de la ventana o dentro del diálogo, permitiendo buscar en el directorio actual o globalmente sin cambiar de contexto.
- **Navegación rápida (Quick Navigation)**: Haz clic central (o doble clic izquierdo) en zonas vacías o en el logotipo incrustado para desplegar un menú en cascada con carpetas abiertas, favoritos, historial y categorías personalizadas.
- **Detección de ruta activa (Active Path Detection)**: Reconoce en tiempo real el directorio físico abierto en la ventana anfitriona, limitando automáticamente el ámbito de búsqueda y resolviendo rutas relativas.

## 2. Componentes nativos de Windows (Integrados de serie)

Compatibles directamente con el motor central de Lertaro sin necesidad de plugins adicionales:

| Tipo de ventana anfitriona | Búsqueda incrustada | Activador de Navegación rápida | Detección de ruta activa |
| :--- | :--- | :--- | :--- |
| **Explorador de archivos de Windows** | Compatible | Doble clic izquierdo o clic central en zonas vacías | Compatible |
| **Diálogos modernos de Abrir/Guardar** | Compatible (Incrustado directamente) | Clic central, o clic izquierdo en el logotipo incrustado | — |
| **Diálogos clásicos de Abrir/Guardar** | Compatible | Clic central, o clic izquierdo en el logotipo incrustado | — |
| **Diálogos clásicos "Buscar carpeta"** | Compatible | Clic central, o clic izquierdo en el logotipo incrustado | — |

> [!NOTE]
> En los diálogos de selección de archivos, Lertaro ya está incrustado en el propio diálogo, por lo que no requiere detección externa de ruta. Al hacer clic en un destino de Navegación rápida, el diálogo salta directamente a esa carpeta.

## 3. Exploradores de terceros profesionales (Plugins opcionales)

Para usuarios avanzados que utilicen exploradores de terceros, Lertaro ofrece plugins de integración dedicados. Actívalos en [**Configuración → Plugins**](./settings/plugins) y configura la Búsqueda incrustada y la Navegación rápida por separado:

| Explorador de archivos | Búsqueda incrustada | Activador de Navegación rápida | Detección de ruta activa | Tecnología y protocolo de comunicación |
| :--- | :--- | :--- | :--- | :--- |
| **Directory Opus** | Compatible | Clic central en la lista de archivos | Compatible | API remota oficial `WM_COPYDATA` |
| **Total Commander** | Compatible | Clic central en la lista de archivos | Compatible | Protocolo de mensajes `WM_COPYDATA` |
| **XYplorer** | Compatible | Clic central en la lista de archivos | Compatible | Comunicación dedicada entre procesos |
| **Files** | Compatible | Clic central en la lista de archivos | Compatible | Marco Windows UI Automation |
| **One Commander** | Compatible | Clic central en la lista de archivos | Compatible | Marco Windows UI Automation |

### Integración avanzada con Directory Opus

- **Columna de tamaño recursivo (Lertaro Tamaño)**: Al activar "Habilitar columna de tamaño Lertaro", se instala una columna de script personalizada en Directory Opus. Lee los tamaños recursivos directamente del índice en memoria de Lertaro, mostrando el tamaño total de todas las carpetas **sin operaciones de E/S en disco**.
- **Guardado permanente**: Tras añadir la columna "Lertaro Tamaño", haz clic en **Carpeta → Formato de carpetas → Guardar → Guardar formato para todas las carpetas**.

### Servicio de compatibilidad con Everything (IPC)

En [**Configuración → General → Sistema**](./settings/general#sistema), activa **Habilitar servicio de compatibilidad Everything (IPC)** para emular la interfaz Win32 IPC de Everything. Herramientas como Directory Opus, Total Commander y Flow Launcher pueden consultar el índice en memoria de Lertaro mediante sus plugins existentes de Everything.

## 4. Diálogos personalizados de software (Plugins dedicados)

Muchos programas profesionales emplean ventanas propias en lugar de los diálogos nativos de Windows. Lertaro proporciona plugins dedicados para estas aplicaciones:

| Aplicación y tipo de diálogo | Búsqueda incrustada | Activador de Navegación rápida | Notas de integración |
| :--- | :--- | :--- | :--- |
| Diálogos Abrir/Guardar de **WPS Office** | Compatible | Clic central o clic en el logotipo | Compatible con WPS Writer, Spreadsheets, Presentation y PDF |
| Diálogo de extracción de **WinRAR** | Compatible | Clic central o clic en el logotipo | Selección rápida del directorio de destino |
| Diálogo de extracción de **Bandizip** | Compatible | Clic central o clic en el logotipo | Especificación inmediata de la carpeta de extracción |
| Diálogo de adición de archivos de **Bandizip** | Compatible | Clic central o clic en el logotipo | Selección rápida de archivos fuente |
| Diálogos Abrir/Guardar de **AutoCAD** | Compatible | Clic central o clic en el logotipo | Optimizado para el almacenamiento y consulta de planos CAD |

Los plugins identifican la jerarquía de controles internos, manteniéndose compatibles con independencia de paquetes de idioma o modificaciones visuales de la interfaz.
