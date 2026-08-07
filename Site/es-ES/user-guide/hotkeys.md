# Atajos de teclado

Todos los atajos globales y de la aplicación que aparecen a continuación se pueden reasignar desde **Configuración
→ Atajos**; aquí se muestran los valores predeterminados. Ver [Configuración → Página de
atajos](./settings/hotkeys-page) para la propia interfaz de configuración.

## Atajos globales

| Acción | Predeterminado | Notas |
|---|---|---|
| Mostrar/ocultar ventana rápida | Doble pulsación de `Ctrl` | También se puede configurar como una combinación completa (por ejemplo, `Alt+Espacio`) en lugar de una doble pulsación. |
| Cambio rápido | `Ctrl+G` | En un diálogo de archivos compatible, navega a la última carpeta obtenida de un gestor de archivos compatible o del Panel Rápido. Una consulta en línea vacía también muestra ese destino como resultado. |
| Seleccionar elemento siguiente | `Ctrl+N` | También funciona como la flecha Abajo literal. En el [Panel Rápido](./settings/quick-panel) recorre la pestaña entera y no un solo grupo: al final de uno continúa en el siguiente. |
| Seleccionar elemento anterior | `Ctrl+P` | También funciona como la flecha Arriba literal. En el Panel Rápido recorre igualmente la pestaña entera, cruzando de un grupo al siguiente. |
| Saltar al resultado 1–9 | `Ctrl` + dígito | El modificador es configurable; el dígito siempre es 1–9. La ventana rápida muestra el atajo de cada resultado visible como una pequeña insignia junto a él, así no tienes que contar filas. |
| Abrir menú de acciones | `Ctrl+O` | También funciona como la flecha Derecha literal sobre un resultado seleccionado. |
| Completar desde la selección | `Ctrl+Tab` | En la ventana rápida, rellena el cuadro de búsqueda con el nombre/ruta del resultado seleccionado. |
| Vista previa QuickLook | `Alt+P` | Alterna el panel de vista previa para el resultado seleccionado. El [Panel Rápido](./settings/quick-panel) usa la misma tecla, y acopla la vista previa al lado suyo donde haya sitio. |
| Historial de palabras clave anterior | `Alt+Arriba` | Recorre hacia atrás tus consultas escritas recientemente. |
| Historial de palabras clave siguiente | `Alt+Abajo` | Recorre hacia delante tus consultas escritas recientemente. |
| Eliminar entrada del historial de palabras clave | `Ctrl+Supr` | |
| Abrir ventana completa | *(ninguno)* | Abre la ventana completa directamente, trasladando la consulta actual — el mismo efecto que hacer clic izquierdo en el [propio logotipo de la ventana rápida](#icono-del-logotipo-en-el-cuadro-de-busqueda) y elegir Mostrar ventana principal en el menú que se abre, sin ese paso adicional. No asignado por defecto; configura uno desde **Configuración → Atajos**. |
| Mantener la ventana abierta | `Ctrl+T` | Impide que la ventana se oculte cuando el foco se va a otro sitio, para poder componer una consulta con texto copiado de otras ventanas: al ocultarse, el cuadro de búsqueda se vaciaría cada vez. Dura lo que dure la invocación actual y termina al ocultarse. En la ventana rápida, un clic con el botón central en el logotipo hace lo mismo, y el logotipo se ilumina mientras está activo; pulsar la tecla de invocación mientras está visible pero sin foco lo devuelve al frente en lugar de ocultarlo. El [Panel Rápido](./settings/quick-panel) usa la misma tecla, con su propio botón de anclaje como indicador visible. |
| Mostrar/ocultar el Panel Rápido | `Ctrl+F2` | Abre el [Panel Rápido](./settings/quick-panel) acoplado en la esquina inferior derecha de la ventana que esté en primer plano, o lo cierra si ya está abierto. Una vez abierto, mantener el modificador de "saltar al resultado 1–9" y pulsar 1–9 cambia de espacio de trabajo. |

## Icono del logotipo en el cuadro de búsqueda

El pequeño icono del logotipo en el cuadro de búsqueda (a la izquierda o a la derecha, según la ventana) hace algo
distinto en cada una de las [tres ventanas](./getting-started#las-tres-ventanas):

- **Ventana rápida** — un clic izquierdo (sin arrastrar) abre el mismo menú que muestra el clic derecho del icono
  de la bandeja del sistema (Mostrar ventana principal, Alternar atajos, Configuración, Acerca de, Salida limpia,
  Salir), anclado en el cursor; el elemento Mostrar ventana principal de ese menú también traslada cualquier
  consulta que tengas escrita en ese momento. Hacer clic y arrastrar mueve la ventana, igual que arrastrar
  cualquier otra parte de la barra de búsqueda — mantén pulsado **Ctrl** mientras arrastras (ya sea la barra o el
  logotipo, y alternar Ctrl a mitad del arrastre también funciona) para restringir el movimiento solo a vertical,
  útil para ajustar la ventana arriba o abajo sin desplazarla lateralmente. El clic derecho restablece la ventana a
  su posición predeterminada en pantalla (no el tamaño) — la misma en la que se centra en el primer arranque. Un
  tooltip al pasar el cursor detalla los tres comportamientos.

  La posición recordada es relativa al monitor en el que estuvo la ventana por última vez, no una coordenada
  absoluta de pantalla — invócala de nuevo en un monitor distinto (o uno con una resolución o escala de PPP
  diferente) y se reabrirá en el punto equivalente ahí en lugar de terminar potencialmente fuera de pantalla o en
  la pantalla equivocada.
- **Ventana en línea** — solo se puede pulsar cuando la ventana está acoplada a un diálogo nativo de
  Abrir/Guardar/Examinar carpetas: un clic izquierdo abre la [navegación rápida](#navegacion-rapida-raton), igual
  que el disparador dedicado de abajo. No se puede pulsar cuando está acoplada a una ventana normal del Explorador
  o al escritorio, ya que en ese caso no hay nada útil a lo que navegar — tampoco aparece ningún resaltado al pasar
  el cursor ni ningún tooltip, así que permanece discreto en lugar de parecer pulsable sin hacer nada.
- **Ventana principal** — ahí el logotipo es puramente decorativo; hacer clic en él no hace nada.

## Navegación rápida (ratón)

El clic central está activado por defecto y el doble clic está desactivado por defecto. Cada disparador se puede cambiar por separado en la configuración:

- Cuando actives este disparador, un **doble clic** en un espacio vacío del escritorio o dentro de una ventana del Explorador activa la navegación rápida.
- **Clic central** en un espacio vacío del escritorio o dentro de una ventana del Explorador — o en el panel de
  lista de archivos de un gestor de archivos de terceros compatible (Directory Opus, Total Commander, XYplorer,
  Files, ...), o en un diálogo nativo de Abrir/Guardar/Examinar carpetas — activa la navegación rápida. Esas otras
  ventanas solo responden al clic central: hacer doble clic ahí ya significa "abrir esto", así que el doble clic no
  se reutiliza. Ver [Gestores de archivos compatibles](./file-manager-support) para saber qué cubre cada
  integración.
- Cuando la ventana de búsqueda en línea está acoplada a un diálogo nativo de Abrir/Guardar/Examinar carpetas, su
  propio logotipo también activa la navegación rápida — ver [Icono del logotipo en el cuadro de
  búsqueda](#icono-del-logotipo-en-el-cuadro-de-busqueda) más arriba.

Cualquiera de estos disparadores abre un menú en cascada con tus Favoritos, Historial y carpetas de acceso rápido
configuradas (ver [Configuración → Favoritos](./settings/favorites) y [Configuración →
Historial](./settings/history)) — los plugins también pueden aportar sus propias entradas, como la propia lista de
carpetas favoritas (Directory Hotlist) de Total Commander si has configurado una en `wincmd.ini`, el propio menú
de Favoritos de Directory Opus, o un [Comando
personalizado](./instant-answers#comandos-personalizados) marcado como "Mostrar en Navegación rápida"
(anidado opcionalmente en un submenú dándole una ruta separada por `/`). Cada plugin que contribuye obtiene su
propia sección etiquetada en la raíz del menú, y el orden en que aparecen esas secciones lo defines tú — ver
[Configuración → General → Navegación rápida](./settings/general#navegacion-rapida). Hacer clic en una carpeta
navega hasta ella en la ventana de destino; hacer clic en un archivo también navega hasta ahí, situándose sobre el
archivo seleccionado dentro de su carpeta contenedora en lugar de abrirlo — la única excepción es el escritorio,
que no tiene un panel de ventana existente en el que navegar, así que ahí una carpeta o archivo se abre
directamente, igual que haría un doble clic. Dentro de un diálogo de archivos en concreto, hacer clic en un archivo
en su lugar salta el diálogo a la carpeta de ese archivo — deliberadamente nunca confirma Abrir/Guardar en tu
nombre.

El plugin **Folder Cascader** es el que realmente construye este menú. Además de Favoritos e Historial (cada uno
activable de forma independiente), tiene su propia lista configurable de carpetas de acceso rápido — desde
**Configuración → Plugins → Folder Cascader → Configurar**, añade la ruta de una carpeta y un nombre para mostrar
opcional, y dale un valor de **Submenú** (por ejemplo, `Herramientas/Red`, separado por `/` para varios niveles)
para anidarla bajo una categoría en lugar de mostrarla en la raíz. Cada nivel del menú — la raíz y cualquier
categoría anidada — también tiene un pequeño botón **+** en su propio encabezado: haz clic en él para añadir ahí
mismo la carpeta que estás explorando en ese momento, rellenada de antemano con su nombre, ruta y la propia ruta de
submenú de ese nivel (todo aún editable antes de confirmar), sin salir del menú para abrir Configuración.

## Teclas fijas (no configurables)

Estas siempre se comportan de la misma forma sin importar tu configuración de atajos:

| Tecla | Contexto | Comportamiento |
|---|---|---|
| `Escape` | En cualquier lugar | Borra el cuadro de búsqueda si tiene texto; en caso contrario, cierra la ventana (o sale del menú de acciones). |
| `Intro` | Lista de resultados | Abre el resultado seleccionado. |
| `Ctrl+Intro` | Lista de resultados | Localiza el resultado en el Explorador en lugar de abrirlo. |
| `Ctrl+Mayús+Intro` | Lista de resultados | Abre el resultado elevado (Ejecutar como administrador). |
| Flecha `Izquierda` / `Derecha` | Menú de acciones | Retroceden un nivel de menú / entran en un submenú. |
| `Retroceso` | Menú de acciones | Sale del menú de acciones cuando el cuadro de búsqueda ya está vacío. |
| `Alt+Espacio` / `Alt+F4` | Todas las ventanas propias de Lertaro | `Alt+Espacio` se suprime en todas ellas: ninguna tiene una barra de título real a la que pueda pertenecer el menú de sistema de Windows. `Alt+F4` cierra la ventana principal y la de configuración con normalidad; sigue suprimido en las ventanas rápida, en línea y QuickLook y en los diálogos, que se muestran y se ocultan en lugar de abrirse y cerrarse. |

## Atajos de acción de plugins

Los plugins pueden registrar sus propias acciones con un atajo predeterminado (por ejemplo, copiar ruta
(`Ctrl+Mayús+C`), ejecutar como administrador, o las acciones de archivo integradas — Cortar `Ctrl+X`, Copiar
`Ctrl+C`, Pegar `Ctrl+V`, Eliminar `Supr`, Eliminar permanentemente `Mayús+Supr`). Estos aparecen en
**Configuración → Atajos → Acciones de plugins**, agrupados por el plugin que los registró, y se pueden reasignar
de la misma forma que los atajos integrados.

## Lista negra de procesos

Si los atajos globales de Lertaro interfieren con otra aplicación (un juego que capture la entrada de teclado en
bruto, por ejemplo), añade ese proceso a la **Lista negra de procesos** — ver [Configuración → Página de
atajos](./settings/hotkeys-page#process-blacklist). Mientras un proceso en la lista negra esté en primer plano,
los atajos globales de Lertaro, la interceptación de pulsaciones de teclado y los disparadores de ratón de
navegación rápida de arriba se dejan pasar todos sin intervenir.

Cualquier aplicación en primer plano que sea realmente de pantalla completa recibe automáticamente el mismo trato
— no hace falta ninguna entrada en la lista negra. En cualquier caso, un diálogo de archivos activo siempre está
exento, así que la navegación rápida sigue funcionando ahí.
