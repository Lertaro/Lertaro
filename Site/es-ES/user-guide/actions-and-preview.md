# Acciones y vista previa instantánea

Lertaro no solo localiza archivos a velocidades ultrarrápidas, sino que integra un completo sistema de acciones contextuales y un potente panel de vista previa instantánea, lo que te permite inspeccionar, gestionar y abrir archivos sin cambiar al Explorador de archivos.

## 1. Menú de acciones en detalle

En la Ventana rápida, pulsa `Ctrl+O` o la flecha derecha `→` sobre el resultado resaltado para desplegar el menú de acciones contextuales; `→` solo cuando el cursor ya está al final de la consulta, de modo que nunca roba el movimiento del cursor mientras editas el texto más atrás. Las mismas teclas funcionan en la tarjeta incrustada **fuera** de los diálogos de archivos; una tarjeta acoplada en un diálogo Abrir/Guardar/Examinar no tiene menú de acciones. En la Ventana principal ninguna de las dos teclas está asignada: haz clic derecho en un resultado, o pulsa `Apps` (`Shift+F10`), para abrir ahí el mismo menú.

No todas las filas tienen menú. No está disponible en la fila **Ver más**, en los resultados aportados por plugins, en los favoritos que son enlaces web, en los resultados instantáneos que ningún proveedor reclama, en las aplicaciones fuera de la Ventana rápida ni en ningún diálogo de archivos — en esas filas las teclas del menú no hacen nada en lugar de abrir un panel vacío.

### Tabla de acciones principales integradas

| Acción | Atajo predeterminado | Descripción |
| :--- | :--- | :--- |
| **Abrir** | `Enter` | Abre el elemento seleccionado o inicia la aplicación con el programa predeterminado del sistema. En la Ventana principal, `Enter` y el doble clic actúan sobre **todas** las filas seleccionadas. |
| **Mostrar en Explorador** | `Ctrl+Enter` | Abre la carpeta contenedora y selecciona el archivo en el Explorador de Windows. |
| **Ejecutar como administrador** | `Ctrl+Shift+Enter` | Inicia la aplicación seleccionada con privilegios de administrador elevados. |
| **Copiar ruta completa** | `Ctrl+Shift+C` | Copia la ruta absoluta (p. ej. `D:\Projects\app.exe`) al portapapeles. |
| **Copiar nombre** | `Shift+C` | Copia al portapapeles los nombres de los archivos o carpetas seleccionados, sin sus rutas. |
| **Copiar archivo** | `Ctrl+C` | Coloca el archivo en el portapapeles, listo para pegarlo en el Explorador o cualquier carpeta. |
| **Cortar / Copiar archivo** | `Ctrl+X` / `Ctrl+C` | Coloca el archivo en el portapapeles, listo para pegarlo en el Explorador o cualquier carpeta. Si hay texto seleccionado en el cuadro de búsqueda, estas teclas siguen siendo órdenes sobre el texto. |
| **Pegar en esta carpeta** | `Ctrl+V` | Cuando una carpeta está resaltada, pega los archivos del portapapeles directamente en ese directorio. Necesita una lista real de archivos en el portapapeles, así que pegar texto en la consulta no se ve afectado. |
| **Eliminar (Papelera de reciclaje)** | `Delete` | Mueve el archivo o carpeta seleccionado a la Papelera de reciclaje de Windows de forma segura. Una tecla aislada solo llega a la acción cuando el cursor ya está al final de la consulta, y después llega la confirmación nativa de la Papelera. |
| **Eliminación permanente** | `Shift+Delete` | Elimina permanentemente el elemento (aviso nativo de "¿eliminar definitivamente?"; después de eso no se puede recuperar). |
| **Cambiar nombre** | — | Cambia el nombre de un único archivo o carpeta existente mediante Windows Shell. El diálogo selecciona de antemano la parte del nombre para facilitar su sustitución. |
| **Menú contextual de Windows** | — | Despliega el menú contextual nativo completo de Windows Explorer (incluyendo opciones de terceros y "Enviar a"). |

### Interacción y filtrado en el Menú de acciones

- **Escribir para filtrar**: Al abrir el menú de acciones, escribe directamente para filtrar por nombre (p. ej., teclear `copy` reduce la lista a las acciones de copiado). El cuadro de filtro se queda con el teclado, así que navegar hacia otro sitio nunca toca tu consulta original.
- **Letra mnemotécnica**: Cada acción puede mostrar una letra resaltada. Pulsarla **ejecuta la acción de inmediato** — no se limita a resaltarla —, pero solo mientras el cuadro de filtro esté aún vacío, de modo que nunca te roba la primera letra que tecleas para filtrar.
- **Cuadro de búsqueda independiente**: El menú de acciones tiene su propio cuadro de búsqueda, que recibe el foco automáticamente al abrirse. Filtrar acciones no cambia la consulta principal. Al cambiar de nivel se borra el filtro de acciones y el cuadro del nuevo nivel recibe el foco.
- **Panel de acciones flotante**: En la Ventana rápida, el panel de Inicio rápido y la Ventana principal, las acciones aparecen en un panel flotante anclado al elemento activo. El panel de Inicio rápido se amplía temporalmente a la altura de trabajo completa de la lista de acciones y vuelve a su tamaño compacto al cerrarse.
- **Navegación jerárquica**: En elementos con submenús (como "Enviar a"), pulsa `→`, `Tab` o `Enter` para entrar; pulsa `←` o `Backspace` (con el filtro vacío) para regresar al nivel superior. En un menú anidado, `Escape` o el clic derecho regresan al nivel superior; en la raíz cierran el menú de acciones.
- **Las teclas de navegación siguen funcionando**: Los atajos configurados de elemento siguiente/anterior (`Ctrl+N` / `Ctrl+P` por defecto) también mueven el resaltado dentro de la lista de acciones, dando la vuelta al primer y último elemento y saltando separadores, encabezados y acciones deshabilitadas. `Tab` entra en un submenú solo si no la has asignado a una de esas teclas.
- **Cerrar al hacer clic fuera**: Al hacer clic fuera de un panel flotante, este se cierra. Si el anfitrión lo permite, hacer clic derecho en otro resultado mientras el panel está abierto reemplaza el objetivo en el mismo lugar.
- **Atajos de acciones**: Mientras el panel tiene el foco puedes usar los atajos definidos por los proveedores. Al ejecutarlos se cierra el panel flotante, pero la Ventana principal o el panel de Inicio rápido permanecen abiertos.

## 2. Características de la lista de la Ventana principal

La Ventana principal de búsqueda (`Ctrl+F`) está diseñada para la gestión masiva de archivos y la exploración profunda:

- **Doble clic en la columna Ruta**: Hacer doble clic en la columna **Nombre** abre el archivo; hacer doble clic en la columna **Ruta** abre directamente la carpeta que lo contiene. Doble clic significa solo el botón *izquierdo*, y un doble clic en el encabezado de una columna maximiza o restaura la ventana.
- **Selección múltiple**: La cuadrícula es una lista normal de Windows: `Ctrl`+clic añade o quita filas, `Shift`+clic toma un rango, y `Enter` o el doble clic actúan entonces sobre **todas** las filas seleccionadas a la vez. El clic derecho en una fila que ya forma parte de una selección conserva toda la selección en lugar de reducirla a esa única fila.
- **Carga continua de resultados en streaming**: Al escanear millones de elementos, los resultados se van añadiendo a la lista en tiempo real sin tener que esperar a que finalice el escaneo completo. Puedes interactuar con las filas al instante, y añadir filas nuevas conserva tu selección y tu posición de desplazamiento; un conjunto de resultados realmente nuevo empieza de nuevo desde arriba.
- **Navegación en bucle**: Pulsar `↑` en la primera fila salta al último elemento; pulsar `↓` en la última fila vuelve al primero. El mismo bucle se aplica en la Ventana rápida y en la tarjeta incrustada: pulsar `↑` sin nada seleccionado todavía cae en la última fila, y `↓` en la primera. Solo el Panel rápido recorre sus grupos como una lista continua **sin** dar la vuelta.
- **Desplazamiento horizontal**: `Shift` + rueda del ratón sobre la cuadrícula desplaza tres columnas por clic de rueda, que es como se alcanzan las columnas más lejanas en una ventana estrecha.
- **Arrastrar archivos hacia fuera**: Cualquier fila puede arrastrarse directamente al Explorador, a un diálogo o a una ventana de chat como una auténtica suelta de archivos; con una selección múltiple, arrastrar uno de sus miembros arrastra **todos**. Soltar fuera de Lertaro oculta la ventana, y el cierre se retiene mientras hay un arrastre en curso para que el cursor de arrastre no quede clavado.
- **Resumen de selección**: Al seleccionar varias filas, la barra de estado muestra el número de elementos seleccionados junto al total de resultados.
- **Vistas previas al pasar el ratón**: Mover el puntero sobre una fila reorienta un panel de vista previa ya abierto sin cambiar la selección, así que puedes ir probando candidatos y pulsar `Enter` en la fila donde te has quedado.
- **Arrastre y memoria de tamaño**: Arrastra la barra superior para reubicar la ventana; las dimensiones ajustadas manualmente se recuerdan automáticamente entre sesiones.

## 3. Vista previa instantánea con QuickLook

Pulsa `Alt+P` sobre un resultado previsualizable para abrir el panel lateral de vista previa acoplado junto a la ventana de búsqueda, o haz **clic central en la fila**, que alterna ese mismo panel. Ninguno de los dos gestos existe en la tarjeta incrustada de los diálogos de archivos; el Panel rápido sí admite ambos.

### La vista previa no tiene teclado

El panel es deliberadamente no activador: nunca le quita el foco a la ventana de búsqueda, así que no hay dónde escribir y `Escape` no lo cierra. Alterna con `Alt+P`, u ocúltalo cerrando la ventana a la que está acoplado. Todos sus controles — la barra de reproducción multimedia, las barras de desplazamiento, las tarjetas de plugins — son solo de ratón, y el foco del teclado se queda exactamente donde estaba.

### Formatos admitidos y funciones avanzadas

- **Imágenes y gráficos vectoriales**: Renderizado nítido y escalado de JPG, PNG, GIF (reproducción animada automática), BMP, WebP, ICO, SVG y más.
- **Documentos y resaltado de código**: Resaltado y formato para TXT, Markdown, JSON, XML, YAML, C#, Python, JS, HTML, etc.
- **Reproducción instantánea de audio y vídeo**: Los archivos multimedia (MP4, MKV, AVI, MOV, WMV, MP3, WAV, FLAC, WMA) **se reproducen automáticamente** con una barra de control integrada que se adapta al tema (reproducir/pausa, barra de progreso, duración, silencio). Se detiene al instante al cambiar de elemento.
- **Inspección de carpetas**: Muestra hasta 30 elementos directos con iconos y tamaños, omitiendo archivos ocultos y del sistema.

### Ajuste de pantalla y gestión de ventanas emergentes

- **Ajuste automático de límites**: Las dimensiones de la vista previa se pueden personalizar en [**Configuración → General → Vista previa**](./settings/general#vista-previa); Lertaro garantiza que nunca sobrepase el área visible del monitor.
- **Lado de acoplamiento**: El panel se acopla a la **derecha** de su ventana de búsqueda y pasa a la izquierda solo cuando a la derecha no cabe; no persigue el lado más espacioso, así que se queda quieto mientras desplazas una ventana ancha. Después sigue a su ventana propietaria cuando la mueves o la redimensionas.
- **La memoria de tamaño dura la sesión**: Arrastrar el asa de redimensionado o mover el panel se recuerda mientras la vista previa siga en juego, pero la próxima vez que la ventana de búsqueda se oculte o se cierre el panel vuelve al tamaño y al lado de acoplamiento configurados.
- **Evitación de diálogos nativos**: Al previsualizar documentos de Office protegidos con contraseña, Lertaro oculta temporalmente sus ventanas para que puedas introducir la contraseña sin bloqueos, restaurándose después con normalidad.
- **Arrastrar desde la vista previa**: La parte superior del panel sirve como origen de arrastre para llevar el archivo previsualizado directamente a editores, navegadores o chats. La barra inferior arrastra el propio panel.

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

## 8. Cambiar nombre

CoreExtensions ofrece la acción **Cambiar nombre** para un único archivo o carpeta existente. El diálogo con el tema del anfitrión muestra el nombre completo actual y selecciona de antemano la parte del nombre: en `report.pdf` solo se selecciona `report`, mientras que en carpetas y nombres sin extensión se selecciona todo el nombre. Pulsa `Enter` para confirmar o `Esc` para cancelar. Tras la confirmación, Windows Shell realiza el cambio de nombre.
