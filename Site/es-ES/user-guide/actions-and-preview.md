# Acciones y vista previa instantánea

Lertaro no solo localiza archivos a velocidades ultrarrápidas, sino que integra un completo sistema de acciones contextuales y un potente panel de vista previa instantánea, lo que te permite inspeccionar, gestionar y abrir archivos sin cambiar al Explorador de archivos.

## 1. Menú de acciones en detalle

Pulsa `Ctrl+O` o la flecha derecha `→` en cualquier resultado de búsqueda (archivo, carpeta, aplicación o elemento de plugin) para desplegar el menú de acciones contextuales. En la Ventana principal, también puedes hacer clic derecho en un resultado para abrir el mismo menú para ese elemento.

### Tabla de acciones principales integradas

| Acción | Atajo predeterminado | Descripción |
| :--- | :--- | :--- |
| **Abrir** | `Enter` | Abre el elemento seleccionado o inicia la aplicación con el programa predeterminado del sistema. |
| **Mostrar en Explorador** | `Ctrl+Enter` | Abre la carpeta contenedora y selecciona el archivo en el Explorador de Windows. |
| **Ejecutar como administrador** | `Ctrl+Shift+Enter` | Inicia la aplicación seleccionada con privilegios de administrador elevados. |
| **Copiar ruta completa** | `Ctrl+Shift+C` | Copia la ruta absoluta (p. ej. `D:\Projects\app.exe`) al portapapeles. |
| **Cortar / Copiar archivo** | `Ctrl+X` / `Ctrl+C` | Coloca el archivo en el portapapeles, listo para pegarlo en el Explorador o cualquier carpeta. |
| **Pegar en esta carpeta** | `Ctrl+V` | Si el elemento seleccionado es una carpeta, pega los archivos del portapapeles en su interior. |
| **Eliminar (Papelera de reciclaje)** | `Delete` | Mueve el archivo o carpeta seleccionado a la Papelera de reciclaje de Windows de forma segura. |
| **Eliminación permanente** | `Shift+Delete` | Elimina permanentemente el elemento (solicita confirmación; no se puede recuperar). |
| **Menú contextual de Windows** | — | Despliega el menú contextual nativo completo de Windows Explorer (incluyendo opciones de terceros y "Enviar a"). |

### Interacción y filtrado en el Menú de acciones

- **Escribir para filtrar**: Al abrir el menú de acciones, escribe directamente para filtrar por nombre (p. ej., teclear `copy` reduce la lista a las acciones de copiado).
- **Cuadro de búsqueda independiente**: El menú de acciones tiene su propio cuadro de búsqueda, que recibe el foco automáticamente al abrirse. Filtrar acciones no cambia la consulta principal. Al cambiar de nivel se borra el filtro de acciones y el cuadro del nuevo nivel recibe el foco.
- **Panel de acciones flotante**: En la Ventana rápida, el panel de Inicio rápido y la Ventana principal, las acciones aparecen en un panel flotante anclado al elemento activo. El panel de Inicio rápido se amplía temporalmente a la altura de trabajo completa de la lista de acciones y vuelve a su tamaño compacto al cerrarse.
- **Navegación jerárquica**: En elementos con submenús (como "Enviar a"), pulsa `→` o `Enter` para entrar; pulsa `←` o `Backspace` (con el filtro vacío) para regresar al nivel superior. En un menú anidado, `Escape` o el clic derecho regresan al nivel superior; en la raíz cierran el menú de acciones.
- **Cerrar al hacer clic fuera**: Al hacer clic fuera de un panel flotante, este se cierra. Si el anfitrión lo permite, hacer clic derecho en otro resultado mientras el panel está abierto reemplaza el objetivo en el mismo lugar.
- **Atajos de acciones**: Mientras el panel tiene el foco puedes usar los atajos definidos por los proveedores. Al ejecutarlos se cierra el panel flotante, pero la Ventana principal o el panel de Inicio rápido permanecen abiertos.

## 2. Características de la lista de la Ventana principal

La Ventana principal de búsqueda (`Ctrl+F`) está diseñada para la gestión masiva de archivos y la exploración profunda:

- **Doble clic en la columna Ruta**: Hacer doble clic en la columna **Nombre** abre el archivo; hacer doble clic en la columna **Ruta** abre directamente la carpeta que lo contiene.
- **Carga continua de resultados en streaming**: Al escanear millones de elementos, los resultados se van añadiendo a la lista en tiempo real sin tener que esperar a que finalice el escaneo completo. Puedes interactuar con las filas al instante.
- **Navegación en bucle**: Pulsar `↑` en la primera fila salta al último elemento; pulsar `↓` en la última fila vuelve al primero.
- **Resumen de selección**: Al seleccionar varias filas, la barra de estado muestra el número de elementos seleccionados junto al total de resultados.
- **Arrastre y memoria de tamaño**: Arrastra la barra superior para reubicar la ventana; las dimensiones ajustadas manualmente se recuerdan automáticamente entre sesiones.

## 3. Vista previa instantánea con QuickLook

Pulsa `Alt+P` en cualquier resultado para abrir el panel lateral de vista previa acoplado junto a la ventana de búsqueda:

### Formatos admitidos y funciones avanzadas

- **Imágenes y gráficos vectoriales**: Renderizado nítido y escalado de JPG, PNG, GIF (reproducción animada automática), BMP, WebP, ICO, SVG y más.
- **Documentos y resaltado de código**: Resaltado y formato para TXT, Markdown, JSON, XML, YAML, C#, Python, JS, HTML, etc.
- **Reproducción instantánea de audio y vídeo**: Los archivos multimedia (MP4, MKV, AVI, MOV, WMV, MP3, WAV, FLAC, WMA) **se reproducen automáticamente** con una barra de control integrada que se adapta al tema (reproducir/pausa, barra de progreso, duración, silencio). Se detiene al instante al cambiar de elemento.
- **Inspección de carpetas**: Muestra hasta 30 elementos directos con iconos y tamaños, omitiendo archivos ocultos y del sistema.

### Ajuste de pantalla y gestión de ventanas emergentes

- **Ajuste automático de límites**: Las dimensiones de la vista previa se pueden personalizar en [**Configuración → General → Vista previa**](./settings/general#vista-previa); Lertaro garantiza que nunca sobrepase el área visible del monitor.
- **Evitación de diálogos nativos**: Al previsualizar documentos de Office protegidos con contraseña, Lertaro oculta temporalmente sus ventanas para que puedas introducir la contraseña sin bloqueos, restaurándose después con normalidad.
- **Arrastrar desde la vista previa**: La parte superior del panel sirve como origen de arrastre para llevar el archivo previsualizado directamente a editores, navegadores o chats.

## 4. Paneles interactivos y texto enriquecido con plugins

QuickLook también admite tarjetas interactivas proporcionadas por plugins:

- **Texto enriquecido adaptado al tema**: Renderizado mediante WebView2 y controles nativos, adaptándose a temas oscuros y claros con tipografía de alto contraste y barras de desplazamiento sutiles.
- **Tarjetas de plugins**: Consultas de diccionarios MDict, pronóstico meteorológico, capturas web y depuración de APIs.

## 5. Puente con QuickLook de terceros (Opcional)

Si utilizas la herramienta externa de código abierto **QuickLook** ([QL-Win/QuickLook en GitHub](https://github.com/QL-Win/QuickLook)), puedes activar el plugin **Puente QuickLook** en [**Configuración → Plugins**](./settings/plugins).

- **Control de vista previa externa**: Se conecta mediante canalizaciones con nombre locales para acoplar la ventana externa de QuickLook directamente junto a Lertaro.
- **Alternativa automática**: Si QuickLook externo no está ejecutándose, Lertaro recurre fluidamente a su motor de vista previa integrado.

## 6. Liberar la ocupación de un archivo

El plugin oficial **Liberar ocupación de archivos** añade una acción disponible al seleccionar un único archivo existente. Muestra los procesos que lo están utilizando, sus PID y las rutas de sus ejecutables, y les solicita que liberen el archivo. La acción no está disponible para carpetas, archivos inexistentes ni selecciones múltiples; el botón de solicitud también se desactiva cuando no se detecta ningún proceso. El diálogo usa el tema del anfitrión, permanece sobre la ventana de búsqueda y queda oculto en Alt+Tab, además de incluir actualización manual.

## 7. Añadir a Favoritos

CoreExtensions ofrece la acción **Añadir a Favoritos** para un único archivo o carpeta existente. Abre un diálogo con el tema del anfitrión para introducir el nombre visible y oculta la acción si la misma ruta ya está en Favoritos.
