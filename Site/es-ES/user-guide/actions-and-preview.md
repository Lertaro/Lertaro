# Menú de acciones y vista previa

## Menú de acciones

Todo resultado — archivo, carpeta o aplicación — tiene un conjunto de acciones más allá de "simplemente abrirlo":
localizar en el Explorador, copiar la ruta, ejecutar como administrador, cortar/copiar/pegar el propio archivo,
eliminarlo (a la Papelera de reciclaje) o eliminarlo permanentemente, y cualquier cosa que añada un plugin (por
ejemplo, el menú contextual completo del shell de Windows, submenús en cascada incluidos).

Ábrelo con el atajo **Abrir menú de acciones** (`Ctrl+O` por defecto) o con la flecha derecha literal sobre un
resultado seleccionado. Dentro del menú:

- **Elemento siguiente/anterior** — las teclas de flecha, o tu propio
  [atajo configurado de elemento siguiente/anterior](./hotkeys), mueven el resaltado arriba y abajo por la lista de
  acciones. Un atajo personalizado (incluso algo como una tecla `Tab` suelta) se respeta aquí exactamente igual que
  en la lista de resultados principal.
- **Flecha derecha / Intro** sobre un elemento con submenú (por ejemplo, un menú en cascada del shell como "Enviar
  a") profundiza en él; **Flecha izquierda** o **Retroceso** (con el cuadro de búsqueda vacío) retrocede un nivel.
- **Escape** sale del menú de acciones, o primero borra el cuadro de búsqueda si habías escrito algo para filtrar
  la lista de acciones.
- Escribe para filtrar las acciones visibles por nombre, de la misma forma que filtrarías los resultados de
  búsqueda.

## Cuadrícula de resultados de la ventana completa

Hacer doble clic en un resultado normalmente lo abre, igual que pulsar Intro — con una excepción: hacer doble clic
en la columna **Ruta** en su lugar abre la carpeta contenedora del resultado en el Explorador, lo mismo que hace
`Ctrl`+Intro en cualquier otro punto de la fila. Una columna personalizada de un plugin puede definir este mismo
tipo de sobrescritura de doble clic para sí misma.

La cuadrícula conserva todas las coincidencias en lugar de una página limitada de ellas, y se va rellenando a
medida que llegan los resultados en lugar de aparecer de golpe cuando termina la búsqueda: una consulta amplia
en una unidad grande puede tardar varios segundos, y las filas se pueden usar durante todo ese tiempo. La
navegación con las flechas da la vuelta en ambos extremos: pulsar ↑ en la primera fila lleva a la última, y ↓ en
la última vuelve a la primera.

La ventana no tiene barra de título, así que su encabezado es la zona de arrastre: pulsa en cualquier punto que
no sea el cuadro de búsqueda ni un botón de ventana y arrastra para moverla. Mientras el puntero está sobre el
encabezado aparece de forma gradual un asa en su parte superior que indica dónde agarrar.

## Vista previa QuickLook

Pulsa el atajo **QuickLook** (`Alt+P` por defecto) sobre un resultado seleccionado para abrir un panel de vista
previa acoplado junto a la ventana de búsqueda — imágenes, documentos y otros tipos de archivo previsualizables se
renderizan sin salir de Lertaro. Púlsalo de nuevo (o muévete a un resultado que QuickLook no pueda previsualizar)
para cerrarlo.

Las vistas previas de carpetas muestran hasta 30 elementos secundarios directos con sus iconos de archivo o carpeta. Los elementos ocultos y del sistema se omiten en esta vista previa aunque el Explorador de Windows esté configurado para mostrarlos.

Los archivos de audio y vídeo — cualquier formato que la reproducción multimedia integrada de WPF admita sin
códecs adicionales (MP4, WMV, AVI, MOV, MP3, WAV, WMA, y algunos otros) — se reproducen automáticamente en cuanto
se abre la vista previa, con una pequeña barra de transporte con el tema de la app (reproducir/pausar, buscar,
tiempo actual/total, silenciar) en lugar de una miniatura estática. Moverse a otro resultado o cerrar la vista
previa detiene la reproducción de inmediato. Un archivo cuyo códec específico no se pueda decodificar recurre en
su lugar a una miniatura estática.

El tamaño de la ventana de vista previa es fijo y configurable por el usuario — ver
[Configuración → General → Vista previa](./settings/general#vista-previa) — e independiente de cuántos resultados
se estén mostrando en ese momento. Sea cual sea el tamaño que establezcas, Lertaro mantiene automáticamente la
ventana de vista previa completamente en pantalla: si no cabe junto a la ventana de búsqueda en tu monitor, se
acopla al lado que tenga espacio, y si el tamaño configurado es mayor que el área utilizable de tu monitor, la
ventana se reduce para ajustarse en lugar de salirse del borde.

Si el archivo que se está previsualizando necesita su propio manejador nativo para mostrar una ventana emergente
propia — lo más habitual, Word o Excel pidiendo una contraseña para un documento cifrado — tanto la ventana rápida
como el panel de vista previa se ocultan mientras esa ventana emergente esté abierta, ya que de lo contrario
quedaría inalcanzable detrás de ellas. Esto no es Lertaro cerrándose ni fallando: resuelve la ventana emergente
(introduce la contraseña, ciérrala, lo que sea que esté pidiendo) y ambas ventanas vuelven exactamente como las
dejaste, texto de búsqueda y selección incluidos.

El encabezado de la ventana de vista previa es en sí mismo un origen de arrastre para el archivo que se está
previsualizando: arrástralo al Explorador, a un editor o a cualquier otro destino y se comporta igual que si
arrastraras la fila del resultado desde la ventana de búsqueda.

### Vista previa interactiva de plugins

Además de archivos y carpetas locales, QuickLook admite paneles de vista previa interactivos personalizados proporcionados por plugins (como definiciones de diccionarios MDict, tarjetas meteorológicas, inspectores de API o vistas previas web de Flow Launcher).

Al seleccionar el resultado de un plugin que proporciona vista previa instantánea, QuickLook activa automáticamente el panel de vista previa y actualiza el contenido de forma fluida al cambiar de selección, sin repetir animaciones de deslizamiento. Para vistas previas basadas en WebView2/HTML, Lertaro aplica automáticamente adaptación de temas, tipografía de alto contraste (claro/oscuro) y barras de desplazamiento translúcidas finas.

Al abrir la ventana completa desde la rápida, el estado de la vista previa se traslada, así que una vista previa
que ya tuvieras abierta permanece abierta.

### Vista previa externa mediante QuickLook (opcional)

Esto es algo distinto del panel de vista previa integrado de arriba, a pesar del nombre compartido: una aplicación
de terceros independiente, también llamada **QuickLook** ([QL-Win/QuickLook en
GitHub](https://github.com/QL-Win/QuickLook), con licencia GPL), que instalas tú mismo — Lertaro no la incluye.

Si está instalada y en ejecución, el plugin (experimental) incluido **QuickLook Bridge** — visible y activable
como cualquier otro plugin en [Configuración → Plugins](./settings/plugins) — la contacta a través de su propia
named pipe y toma el control de la vista previa para todo, por delante de cada tipo de vista previa integrada
descrita más arriba. El propio panel de vista previa de Lertaro se oculta mientras esto está activo, y la propia
ventana flotante de QuickLook se mueve exactamente al lugar que ese panel habría ocupado — visualmente se
interpreta como "el panel de vista previa se convirtió en QuickLook", aunque técnicamente la ventana de QuickLook
sigue siendo una ventana de nivel superior completamente independiente que Lertaro reposiciona para que la siga.

Una entrada del menú de acciones, **Vista previa en QuickLook**, te permite enviar un resultado a QuickLook
manualmente incluso cuando algún otro tipo de vista previa ganaría de otro modo para ese resultado.

Dado que esto depende por completo del protocolo interproceso propio (no documentado, privado) de QuickLook, y no
de ninguna API de integración publicada, una futura versión de QuickLook podría cambiar ese protocolo y romper
esto en silencio. Desinstalar o cerrar QuickLook no tiene efecto sobre ninguna otra cosa — Lertaro simplemente
recurre a sus propios tipos de vista previa integrados.
