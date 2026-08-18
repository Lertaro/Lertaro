# Panel Rápido

Mientras el panel está abierto, la carpeta mostrada en el encabezado del grupo seleccionado también se trata como el directorio actual. No importa si el elemento seleccionado es un archivo o una carpeta: si la ruta del encabezado no está vacía, abre un diálogo de archivos compatible y usa Cambio rápido (por defecto, `Ctrl+G`) para ir allí.

Un panel flotante que se invoca con una tecla y se acopla en la esquina inferior derecha de la ventana que esté en
primer plano, con la mitad de su alto y la mitad de su ancho. Muestra las carpetas que le indiques, y las listas que
aportan tus plugins — como miniaturas o como lista — para llegar a archivos, arrastrarlos fuera o soltarlos dentro sin salir de la ventana en la que estás
trabajando. Arrastra su borde superior para moverlo a otro sitio durante la invocación actual.

- **Activar el panel rápido** — interruptor general; apagado, la tecla no hace nada.

La tecla que lo invoca está en la página de [Atajos](./hotkeys-page) (`Ctrl+F2` por defecto).

El panel no puede reducirse por debajo de 280 × 200 píxeles, para que sus pestañas, el cuadro de filtro y el contenido sigan siendo utilizables. Si una ventana de Lertaro ya está en primer plano, la tecla de invocación se ignora deliberadamente en lugar de abrir otro panel sobre Lertaro; si el panel ya está abierto, la misma tecla todavía lo cierra.

La página bajo ese interruptor se divide en dos: **Espacios de trabajo**, los conjuntos de carpetas que reúnes tú, y
**Pestañas de plugin**, las que aportan tus plugins. Ambas acaban en la misma franja de pestañas.

## Espacios de trabajo

El panel muestra una pestaña cada vez, y un **espacio de trabajo** es una de ellas. Un
espacio de trabajo es un conjunto de fuentes reunido para un tipo de trabajo: las carpetas de un proyecto, el sitio
donde guardas material de referencia, una bandeja de entrada donde vas dejando cosas.

La lista de la izquierda son los espacios de trabajo, con los botones **Nuevo espacio de trabajo**, **Duplicar
espacio de trabajo** y **Eliminar espacio de trabajo**, y la misma lista con flechas arriba/abajo (o arrastrar para
reordenar) que se usa en el resto de la Configuración (ver [Favoritos](./favorites)). De arriba abajo aquí es de
izquierda a derecha en la franja de pestañas del panel.

- **Nombre** — lo que se lee en su pestaña. Si se deja vacío, recurre a un nombre por defecto traducido, así que un
  espacio de trabajo al que nunca se le cambió el nombre sigue el idioma de la interfaz.
- **Activado** — la casilla junto a cada espacio de trabajo. Apagada, mantiene la configuración pero le quita la
  pestaña; pensado para uno preparado para un trabajo que este mes no estás haciendo, donde borrarlo significaría
  rehacer la lista de fuentes. La **×** de una pestaña del panel en vivo hace exactamente esto, y por eso volver a
  activarlo se hace aquí.

El espacio de trabajo seleccionado se edita en dos subpestañas: **Fuentes** y **Aplicaciones**.

## Fuentes

Cada fuente es un grupo del panel, mostrado en el orden de esta lista. **Añadir carpeta** elige una; la casilla de
cada fila oculta ese grupo sin quitarlo; el cuadro de nombre sustituye el encabezado del grupo (déjalo vacío para el
nombre propio de la carpeta); y **Más opciones** abre el resto:

- **Mostrar** — de qué tira el grupo dentro de la carpeta:
  - **Archivos cambiados recientemente** — solo lo que ha cambiado hace poco, lo más nuevo primero, respondido
    desde el índice y no recorriendo la carpeta.
  - **Todo, lo más nuevo primero** — nunca oculta un archivo por antigüedad, solo decide qué va antes.
  - **Todo, por nombre** — una carpeta usada como barra de accesos directos.
- **Carpeta** — la carpeta en sí, con un botón **…** para buscarla.
- **Incluir subcarpetas** — desactivado por defecto.
- **Aceptar archivos soltados** — los archivos y carpetas que arrastres sobre este grupo se copian dentro de su
  carpeta, usando la propia copia de archivos de Windows (su diálogo de progreso, sus avisos de conflicto, su
  deshacer). Siempre una copia, nunca un movimiento. Desactivado por defecto y preguntado por fuente: una carpeta
  que usas como bandeja de entrada lo quiere, y una de la que solo lees, no.
- **Filtro** — limita opcionalmente lo que muestra esta fuente con uno o más patrones separados por `;` o `,` (p. ej. `*.mp4;*.mkv`). Déjalo vacío, que es el valor predeterminado, para no filtrar.
- **Como máximo** — cuántas entradas muestra el grupo. 0 significa todo lo que tenga la fuente.
- **Cambiado en (minutos)** — solo cuentan las entradas cambiadas dentro de ese tiempo. 0 significa sin límite de
  antigüedad.
- **Mostrar como lista** — el grupo se abre como lista de detalles en vez de miniaturas. Cuál conviene es una
  propiedad de la carpeta: las imágenes quieren miniaturas, los documentos quieren nombres y fechas. Las
  miniaturas se **reparten** la fila entre cinco: un panel acoplado a una ventana ancha obtiene miniaturas que
  merece la pena mirar en lugar de más miniaturas. Cinco no es un número fijo: se ponen más en cuanto ni
  siquiera cinco cabrían sin superar lo que una miniatura puede llenar, y menos en un panel demasiado estrecho
  para esas cinco legibles. Sean las que sean, la fila se reparte en vez de llenarse con piezas de tamaño fijo,
  así que nunca termina en un hueco. Cada imagen conserva su propia forma, de modo que una carpeta de vídeo
  16:9 no se convierte en una cuadrícula de cajas altas con una franja vacía en cada una.

Una carpeta añadida aquí empieza en **Todo, por nombre**: una carpeta elegida a mano casi siempre es un sitio
donde se guardan cosas. El espacio de trabajo con el que llega una instalación nueva es la excepción, y a
propósito: Escritorio, Descargas y Documentos son sitios a los que las cosas llegan, y empiezan en archivos
cambiados recientemente.

## Pestañas de plugin

Pestañas aportadas por plugins, listadas en la segunda pestaña de la propia página y no dentro de un espacio de
trabajo: lo que ofrece un plugin es una colección entera, y no tiene más que ver con las carpetas de un espacio de
trabajo que con las de otro. Cada una se marca para meterla en la franja o se desmarca para sacarla, una sola vez y
para el panel entero.

CoreExtensions trae cinco:

| Pestaña | Qué lista |
|---|---|
| **Favoritos** | Tus [Favoritos](./favorites), en el orden en que los colocaste. Las direcciones web se abren en el navegador. |
| **Historial** | Lo que has abierto desde el propio Lertaro, lo más reciente primero: la única de estas que incluye aplicaciones. |
| **Elementos recientes de Windows** | La propia lista de documentos recientes del shell, resuelta a los archivos a los que apunta, lo más nuevo primero. |
| **Última carpeta** | La carpeta que estabas mirando por última vez, sea en el Explorador o en el diálogo de archivos de cualquier aplicación, para que el panel te devuelva la carpeta de la que acabas de venir. |
| **Archivos recientes** | Los archivos más nuevos de las carpetas que el panel tiene configuradas para vigilar, respondido desde el índice en lugar de recorriéndolas. Solo archivos. |

- La casilla mete la pestaña en la franja o la saca. La **×** de una pestaña viva hace lo mismo, y por eso volver a
  marcarla se hace aquí.
- **Mostrar como lista** — la pestaña se abre como lista de detalle en lugar de como miniaturas, la misma elección
  que tiene una fuente de carpeta, preguntada aquí porque una pestaña de plugin no tiene una fila en ninguna lista
  de fuentes donde preguntarla. El conmutador del propio encabezado del panel la sigue anulando mientras el panel
  esté abierto.
- Solo se listan las pestañas cuyo componente de plugin está habilitado en [Plugins](./plugins), y esa es otra
  pregunta: desmarcar aquí saca la pestaña de la franja, deshabilitar allí impide que el proveedor se ejecute
  siquiera. Una pestaña cerrada mientras su plugin estaba apagado sigue cerrada cuando el plugin vuelve — el estado
  se guarda contra el componente, no se reconstruye a partir de lo que resulte estar cargado.

Una pestaña de plugin contiene una sola lista y no lleva encabezado: la pestaña ya tiene su nombre. No acepta
archivos soltados, y un plugin que no devuelve nada no llega a tener pestaña.

## Aplicaciones

Aplicaciones a las que pertenece este espacio de trabajo, un nombre de proceso por línea (`chrome` o `chrome.exe`,
da igual). Invoca el panel sobre una de ellas y se abrirá en este espacio de trabajo en lugar de donde se quedó — la
aplicación en la que ya estás dice de qué conjunto de carpetas hablas. Vacío, al espacio de trabajo solo se llega a
mano.

## Solo panel rápido

Aplicaciones de las que el panel se mantiene alejado, un nombre de proceso por línea. Se **suma** a la
[lista negra de procesos](./hotkeys-page#process-blacklist) global en vez de sustituirla: lo bloqueado globalmente
también lo está aquí. Esta lista es para las aplicaciones que solo este panel tiene motivos para evitar — se acopla
sobre la ventana en primer plano, así que arruina un reproductor a pantalla completa o un juego sin que estos
merezcan un bloqueo global.

## Usar el panel

- **Cuadro de filtro** — a la derecha de la franja de pestañas, con el foco puesto en cuanto se abre el panel.
  Empareja de forma difusa (la misma coincidencia estilo fzf que usa la ventana de búsqueda, alias de pinyin
  incluidos) y solo dentro del espacio de trabajo actual. Un grupo sin nada que coincida se oculta mientras el
  filtro esté puesto.
- **Enter** abre lo que esté seleccionado, que es lo mismo que hace un doble clic. La primera entrada está
  seleccionada desde el principio, así que una invocación se resuelve escribiendo y pulsando Enter sin salir nunca
  del cuadro de filtro.
- **Arriba/Abajo** mueven la selección y cruzan de un grupo al siguiente en vez de detenerse en la última fila de
  uno: los grupos se leen como una sola lista de arriba abajo. Funcionan desde el cuadro de filtro sin sacarle el
  teclado, así que filtrar y elegir son el mismo gesto. Dentro de un grupo mostrado como miniaturas siguen
  moviéndose fila a fila, tal como está dispuesta la cuadrícula. Los atajos configurados de "seleccionar elemento
  siguiente/anterior" (`Ctrl+N`/`Ctrl+P` por defecto) hacen lo mismo, salvo que haya una acción de plugin asignada
  a esa misma tecla, que tiene prioridad.
- **Cambiar de pestaña** — mantén el modificador de "saltar al resultado N" (`Ctrl` por defecto, ver
  [Atajos](../hotkeys)) y pulsa 1–9, o haz clic en una pestaña. Los espacios de trabajo y las pestañas de plugin
  comparten una sola franja y un solo orden; las pestañas se arrastran para reordenarlas, y cada una tiene una **×**
  que la saca de la franja (apagando el espacio de trabajo, o cerrando la pestaña de plugin: ambas cosas se deshacen
  en los ajustes de arriba). Cerrar la última cierra el panel.
- **Los encabezados de grupo** llevan un conmutador de orden (por nombre / por fecha de modificación), uno de vista (miniaturas / lista) y una flecha para plegar. Haz clic en el encabezado o en su flecha para expandir o plegar el grupo, como en un encabezado estándar de Windows; los controles de orden y vista conservan sus propias acciones. Lo que hagas aquí dura mientras el panel esté abierto; el estado inicial es el que digan los ajustes de arriba.
- **Números de línea** — todos los grupos comparten el margen izquierdo. La vista de lista numera elementos; la de mosaicos numera filas visuales. Su anchura sigue al mayor número visible, por lo que queda alineado. Desplazarse nunca cambia el estado expandido de un grupo: usa su flecha para abrirlo o cerrarlo.
- **Seleccionar varios** — en vista de miniaturas, arrastra un recuadro por el espacio vacío para hacer una
  selección. La selección pertenece a un solo grupo, ya que cada grupo dibuja su propia lista; hacer clic en el
  espacio vacío la limpia.
- **Soltar archivos dentro** — arrastra archivos, carpetas o una imagen directamente desde una página web sobre un
  grupo que acepte archivos soltados. Se acepta cualquier cosa que el arrastre pueda ofrecer como archivo, no solo
  imágenes. El grupo se recarga solo cuando termina la copia.
- **Vista previa** — la tecla de QuickLook (`Alt+P` por defecto, reasignable en la página de
  [Atajos](./hotkeys-page) como cualquier otra) abre la ventana de vista previa del archivo
  seleccionado, y a partir de ahí sigue a la selección. Se acopla a la derecha del panel, y solo
  pasa a su izquierda cuando el borde de la pantalla no deja sitio ahí: justo lo que le ocurre a un panel
  en la esquina inferior derecha de una ventana maximizada. Hacer clic dentro de la vista previa no cierra el panel; alejarse de ambos, sí. Se cierra
  junto con el panel.
- **Que no se cierre** — el panel se cierra al perder el foco. El botón de anclaje, o el atajo de "mantener la
  ventana abierta" (`Ctrl+T` por defecto, el mismo que usa la ventana rápida), lo suspende durante la invocación
  actual.
- **Esc** vacía el cuadro de filtro si tiene texto, y cierra el panel si no.
- El panel se mantiene al día solo: una carpeta que esté mostrando y cambie en disco se recarga, a través de la
  misma vigilancia basada en el índice que usa el resto de la aplicación, no de un escaneo propio.
