# Atajos de teclado y gestos

Lertaro adopta una filosofía de interacción centrada en el teclado, complementada con prácticos gestos de ratón y navegación rápida en cascada. Excepto las teclas fijas, todos los atajos globales y de la aplicación se pueden personalizar en [**Configuración → Atajos de teclado**](./settings/hotkeys-page).

## 1. Tabla resumen de atajos globales

| Acción | Atajo predeterminado | Descripción y detalles de interacción |
| :--- | :--- | :--- |
| **Mostrar/Ocultar Ventana rápida** | Doble pulsación de `Ctrl` | Configurable en modo doble pulsación o combinación estándar (p. ej. `Alt+Space`, `Win+Space`). Una doble pulsación son dos pulsaciones del **mismo** modificador, soltadas con **100–300 ms** de diferencia: la `Ctrl` izquierda y la derecha son teclas distintas, y cualquier otra tecla pulsada en medio reinicia el conteo. En la forma de doble pulsación el modificador nunca se consume, así que los atajos normales del estilo `Ctrl`+`C` siguen funcionando. Si se activa **Abrir el panel principal de forma predeterminada**, este atajo abre la Ventana principal: al mostrarla por primera vez solo la lleva al primer plano y le da el foco una vez; si está visible pero inactiva la vuelve a activar, y si ya está activa la cierra al pulsar de nuevo. No se mantiene automáticamente siempre encima. |
| **Salto rápido (Quick Jump)** | `Ctrl+G` | En cuadros de diálogo, salta directamente a la carpeta consultada recientemente en exploradores o en el Panel rápido. |
| **Menú de Navegación rápida** | Sin valor predeterminado | Un atajo global opcional abre el menú de Navegación rápida en cascada. Desde el escritorio o una aplicación normal usa el contexto del escritorio; en el Explorador de archivos y los cuadros de diálogo nativos usa el contexto de la ventana activa. Los exploradores y los cuadros de diálogo siguen permitidos aunque la protección normal de primer plano suprima los atajos globales. |
| **Seleccionar elemento siguiente** | `Ctrl+N` o `↓` | Mueve el resaltado hacia abajo. En el Panel rápido se desplaza fluidamente entre grupos. En el Panel de inicio rápido, las flechas siguen la cuadrícula visible: **←** y **→** atraviesan los límites de las filas, mientras **↑** y **↓** buscan el elemento más cercano de la misma columna en la dirección correspondiente, incluso entre grupos. Si no existe ningún elemento de esa columna en toda la dirección adyacente, la selección permanece donde está. Con el Panel de inicio rápido visible y la consulta vacía, `Ctrl+N` recorre la siguiente fuente y vuelve a la primera al llegar al final. |
| **Seleccionar elemento anterior** | `Ctrl+P` o `↑` | Mueve el resaltado hacia arriba. En el Panel rápido también se desplaza entre grupos. En el Panel de inicio rápido, las flechas siguen la cuadrícula visible: **←** y **→** atraviesan los límites de las filas, mientras **↑** y **↓** buscan el elemento más cercano de la misma columna en la dirección correspondiente, incluso entre grupos. Si no existe ningún elemento de esa columna en toda la dirección adyacente, la selección permanece donde está. Con el Panel de inicio rápido visible y la consulta vacía, `Ctrl+P` recorre la fuente anterior y vuelve a la última al llegar al principio. |
| **Saltar a resultados 1–9** | `Ctrl` + `1`–`9` | Modificador personalizable. Aparecen distintivos numéricos junto a los resultados para apertura instantánea. El número indica la N.ª fila seleccionable **a partir de la primera fila visible**, así que los distintivos se renumeran al desplazar; la `0` no está asignada. El panel de Inicio rápido es la excepción: asigna `1`–`9`, `0` y `A`–`Z` para hasta 36 mosaicos. |
| **Abrir Menú de acciones** | `Ctrl+O` o `→` | Despliega el menú contextual de acciones (copiar ruta, propiedades, ejecutar como admin, operaciones de archivo, etc.). Funciona en la Ventana rápida y en la tarjeta incrustada **fuera** de los diálogos de archivos; la Ventana principal abre el mismo menú con el clic derecho o la tecla `Apps` (`Shift+F10`). |
| **Autocompletar desde selección** | `Ctrl+Tab` | Rellena la barra de búsqueda con el nombre o ruta completa del elemento seleccionado para refinamiento. |
| **Vista previa instantánea QuickLook** | `Alt+P` | Abre/cierra el panel lateral de vista previa (imágenes, documentos, reproducción de audio/vídeo, árboles de carpetas). Disponible en la Ventana rápida, la Ventana principal y el Panel rápido, no en la tarjeta incrustada; hacer clic central en una fila previsualizable alterna el mismo panel. |
| **Término de búsqueda anterior** | `Alt+Up` | Retrocede por el historial reciente de búsquedas. |
| **Término de búsqueda siguiente** | `Alt+Down` | Avanza por el historial reciente de búsquedas. |
| **Eliminar término de historial** | `Ctrl+Delete` | Elimina la palabra clave mostrada actualmente del historial de búsqueda. |
| **Abrir Ventana principal** | `Ctrl+F` | Abre la ventana principal de gran tamaño manteniendo la búsqueda actual. |
| **Abrir ventana LocalSend** | `Ctrl+S` | Abre la ventana de transferencia inalámbrica LocalSend para enviar archivos o texto a otros dispositivos. |
| **Fijar ventana (Mantener visible)** | `Ctrl+T` | Bloquea la ventana abierta al perder el foco (ideal para pegar búsquedas de varias fuentes). |
| **Mostrar/Ocultar Panel rápido** | `Ctrl+F2` | Acopla el panel rápido junto a la ventana activa con archivos recientes, favoritos y espacios de trabajo. |

### Búsqueda en línea vacía en diálogos de archivos

Cuando un cuadro de búsqueda en línea está integrado en un diálogo de archivos nativo y la consulta está vacía, la lista muestra primero el grupo **Directorio anterior**. Si está activado **Mostrar las carpetas abiertas actualmente en la búsqueda integrada**, también muestra el grupo **Carpetas abiertas actualmente**, recopilado desde los exploradores compatibles. Se excluye la carpeta actual del diálogo, las rutas duplicadas se unifican, los grupos vacíos permanecen ocultos y los encabezados de grupo no muestran insignias de atajos. La opción está activada de forma predeterminada y se puede cambiar en [**Configuración → General → Sistema**](./settings/general). Este comportamiento solo se aplica a la búsqueda en línea de los diálogos de archivos; las ventanas Rápida y Completa no cambian.

### Invocación y traspaso del foco en la Ventana incrustada

Una tarjeta acoplada en un diálogo de archivos nativo **no roba el teclado** a propósito: el diálogo conserva el foco, así que sigues escribiendo en su propio campo de nombre de archivo mientras la tarjeta refleja la consulta. Los gestos que mueven el cursor son:

| Gesto | Efecto | Condiciones |
| :--- | :--- | :--- |
| **Escribir una letra o un dígito** | Invoca la tarjeta y lleva ese primer carácter al cuadro de búsqueda; las pulsaciones siguientes siguen entrando | Solo pulsaciones reales: la entrada sintetizada por herramientas de automatización pasa intacta, y no se captura nada mientras haya un menú contextual o del sistema abierto |
| **Doble pulsación de `Ctrl`** (o el atajo de invocación que configures) | Coloca el cursor en el cuadro de búsqueda propio de la tarjeta. Cuando el cuadro ya tiene el cursor y la consulta está vacía, en cambio borra la consulta y devuelve el foco al campo del diálogo, manteniendo la tarjeta abierta | Es el mismo atajo que en cualquier otra parte, así que reasignarlo en [**Configuración → Atajos de teclado**](./settings/hotkeys-page) también mueve esto. Un diálogo de archivos sigue permitido incluso cuando una lista negra o la pantalla completa suelen silenciar los atajos globales |
| **`Escape`** | Mientras el **diálogo** tiene el teclado, la tarjeta se cierra. Mientras lo tiene la **tarjeta**, se borra la consulta y el foco vuelve al diálogo (en un diálogo de archivos) o la tarjeta se cierra (acoplada sobre una ventana normal del Explorador) | |
| **`Backspace`** en un cuadro vacío | Abandona la búsqueda exactamente igual que `Escape` | Solo cuando la tarjeta tiene el cursor; mientras el diálogo tiene el teclado la tecla solo edita la consulta de la tarjeta |
| **`Enter`** en un cuadro vacío | Abandona la búsqueda como `Escape` | Solo cuando la tarjeta tiene el cursor: mientras el diálogo tiene el teclado, `Enter` abre la fila de navegación resaltada |
| **`Tab`** | Siempre queda como el recorrido de controles propio del diálogo, así que llega a la tarjeta con el atajo de invocación | |

> [!NOTE]
> Dentro de un diálogo de archivos el menú de acciones no está disponible para la tarjeta (`Ctrl+O`, `→` y el clic derecho no hacen nada ahí), y tampoco se ofrece la vista previa con `Alt+P`. El ratón sigue funcionando sobre las filas: el **clic izquierdo** abre el resultado resaltado, **`Ctrl`+clic** lo abre como administrador, una fila se puede **arrastrar hacia fuera** hacia el diálogo y simplemente **pasar el ratón por encima** mueve la selección, que el explorador anfitrión refleja.

## 2. Icono de búsqueda y gestos de ratón

El pequeño logotipo en la barra de búsqueda no es solo estético: ofrece múltiples gestos rápidos:

### Gestos del icono en la Ventana rápida

- **Clic izquierdo**: Despliega el menú contextual principal (Mostrar ventana principal, Cambiar atajo, Configuración, Acerca de, Salida limpia, Salir). "Mostrar ventana principal" transfiere tu búsqueda activa.
- **Clic izquierdo y arrastrar**: Arrastra la barra de búsqueda para reubicarla. **Mantener `Ctrl` mientras arrastras** bloquea el movimiento al **eje vertical**, manteniendo la alineación horizontal intacta.
- **Clic derecho**: Restablece instantáneamente la Ventana rápida a su posición central predeterminada en pantalla sin alterar dimensiones.
- **Clic central**: Alterna el estado "Fijar ventana". El logotipo se ilumina mientras está fijada.

> [!NOTE]
> Las coordenadas recordadas para la Ventana rápida son **coordenadas relativas proporcionales** a ese monitor. Al invocarla en otra pantalla con distinta resolución o escala DPI, Lertaro recalcula la posición automáticamente dentro de los límites visibles.

### Iconos en la Ventana incrustada y Ventana principal

- **Ventana incrustada**: Al incrustarse en diálogos nativos (Abrir/Guardar/Examinar), hacer clic izquierdo en el icono abre el menú de [**Navegación rápida**](#3-navegacion-rapida-activadores-de-raton); desactivado en el Explorador ordinario. Arrastrar el icono mueve la propia tarjeta, y el desplazamiento se vuelve a aplicar en cada acoplamiento posterior.
- **Ventana principal**: Al hacer clic izquierdo en el icono se abre el menú contextual; **Mostrar ventana principal** se oculta porque la ventana ya está abierta. El clic central alterna el estado de fijación de la ventana.

### Gestos del cuadro de búsqueda (Ventana rápida)

- **Rueda del ratón sobre el cuadro**: Retrocede o avanza por los términos de búsqueda recientes, la misma navegación que `Alt+Up` / `Alt+Down` y sin quitar la mano del ratón.
- **Clic central en el cuadro**: Elimina la entrada del historial que se está mostrando. Este gesto es fijo y no es la asignación configurable de `Ctrl+Delete`.
- **Escribir en cualquier momento** termina la sesión del historial, así que el siguiente clic de rueda empieza de nuevo desde tu consulta actual. El historial no da la vuelta: el término más antiguo se detiene ahí, y el más reciente devuelve la consulta desde la que empezaste.

### Icono de la bandeja del sistema

- **Clic izquierdo**: Alterna la Ventana rápida, exactamente igual que el atajo de invocación.
- **Clic derecho** (o un segundo clic, ya que no existe controlador de doble clic): Abre el menú en el cursor: Mostrar ventana principal, Enviar a otros dispositivos (solo mientras LocalSend esté activo), Desactivar/Activar atajos, Configuración, Acerca de, Salida limpia (solo cuando Lertaro es el único proceso en ejecución) y Salir.
- **Ocultar icono de la bandeja del sistema** ([**Configuración → General → Sistema**](./settings/general)) quita el icono, pero nunca el menú del icono dentro de la ventana. Mientras los atajos estén desactivados desde ese menú, el icono se muestra de nuevo a la fuerza para que no puedas quedarte fuera.

## 3. Navegación rápida (Activadores de ratón)

La Navegación rápida te permite acceder a directorios frecuentes y archivos recientes únicamente con clics de ratón, sin teclear.

También puedes asignar un atajo de teclado global opcional en [**Configuración → Atajos de teclado**](./settings/hotkeys-page). Desde el escritorio o una aplicación normal abre el menú con el contexto del escritorio; en el Explorador de archivos y los cuadros de diálogo nativos usa el contexto de la ventana activa.

### Entornos compatibles

- **Espacio vacío del Escritorio**: Clic central (o doble clic izquierdo opcional) para abrir el menú. Al hacer clic en una carpeta o archivo se abre directamente.
- **Explorador de archivos**: Clic central en zonas vacías del Explorador; al hacer clic en un elemento, la ventana navega directamente a esa carpeta.
- **Exploradores de terceros**: Clic central en la lista de archivos de Directory Opus, Total Commander, XYplorer, Files y One Commander (ver [**Exploradores de archivos compatibles**](./file-manager-support)).
- **Diálogos de archivo**: Clic central o clic en el icono incrustado dentro de diálogos Abrir/Guardar para saltar a la carpeta de destino sin confirmar accidentalmente.

### Estructura del menú en cascada

Impulsado por el plugin **Folder Cascader**:

1. **Carpetas abiertas actualmente**: Agrupa y desduplica carpetas activas de todos los exploradores abiertos.
2. **Favoritos e Historial**: Muestra elementos marcados con estrella e historial de visitas recientes.
3. **Categorías personalizadas**: Configura submenús anidados en **Configuración → Plugins → Folder Cascader** (p. ej. `Trabajo/ProyectoA`).
4. **Añadir carpeta rápida (Botón `+`)**: Cada encabezado de submenú cuenta con un botón `+` para guardar el directorio activo directamente en esa categoría.

## 4. Teclas fijas básicas (No configurables)

Para garantizar un comportamiento coherente y determinista, las siguientes teclas actúan igual en todas las configuraciones:

| Tecla | Contexto | Comportamiento estándar |
| :--- | :--- | :--- |
| `Enter` | Lista de resultados | Abre el elemento seleccionado (archivo, carpeta, app o acción). En la Ventana principal abre **todas** las filas seleccionadas. En un cuadro de búsqueda incrustado vacío, en cambio, abandona la búsqueda: no hay ninguna mejor coincidencia que abrir. |
| `Ctrl+Enter` | Lista de resultados | Muestra y selecciona el elemento en el Explorador de archivos de Windows. |
| `Ctrl+Shift+Enter` | Lista de resultados | Ejecuta la aplicación seleccionada con privilegios de administrador. |
| `Escape` | Ventana rápida | Oculta la ventana. Nunca vacía el cuadro en esa pulsación: la consulta se conserva o se descarta según **Mantener el contenido del cuadro de búsqueda al cerrar**. |
| `Escape` | Ventana principal | Borra la consulta y vuelve a enfocar el cuadro; cierra la ventana cuando el cuadro ya está vacío, o directamente cuando **Mantener el contenido del cuadro de búsqueda al cerrar** está activado (así no tienes que pulsarlo dos veces en vano). |
| `Escape` | Tarjeta incrustada | Consulta **Invocación y traspaso del foco en la Ventana incrustada** más arriba: cierra la tarjeta mientras el diálogo tiene el teclado y devuelve el foco mientras lo tiene la tarjeta. |
| `Escape` | Menú de acciones | Una pulsación por nivel: borra el filtro de acciones, luego sale del submenú y después abandona el menú y restaura la consulta original. |
| `Backspace` | Menú de acciones | Sale del menú de acciones hacia la lista de resultados cuando el filtro está vacío. |
| `←` / `→` Flechas | Menú de acciones | Flecha izquierda vuelve al menú superior; flecha derecha entra en submenús. `Tab` entra en un submenú también, salvo que la hayas asignado al atajo de elemento siguiente/anterior. |
| `Tab` | Tarjeta incrustada | Se consume para que el foco no pueda salir del cuadro de búsqueda, mientras que `Tab` dentro del diálogo sigue recorriendo sus propios controles como siempre. |
| `Tab` | Menú de acciones | Entra en el submenú resaltado, salvo que hayas asignado `Tab` al atajo de elemento siguiente/anterior, que gana. |
| `Apps` / `Shift+F10` | Ventana principal | Abre el menú de acciones de la selección actual, exactamente igual que el clic derecho. |
| `Alt+Space` | Todas las ventanas | Bloqueado para evitar activar el menú del sistema en ventanas sin marco. |
| `Alt+F4` | Ventanas Principal / Config | Cierra la ventana normalmente; bloqueado en las ventanas Rápida, Incrustada, Panel rápido, Vista previa, LocalSend y los diálogos internos de la aplicación. |

## 5. Atajos de acciones de plugins y lista negra

### Atajos de acciones de plugins

Las acciones de archivo integradas llegan con estos valores predeterminados: Cortar `Ctrl+X`, Copiar archivo `Ctrl+C`, Pegar `Ctrl+V`, Eliminar `Delete`, Eliminación permanente `Shift+Delete`, Copiar ruta completa `Ctrl+Shift+C`, Copiar nombre `Shift+C`, Mostrar en Explorador `Ctrl+Enter`, Ejecutar como administrador `Ctrl+Shift+Enter`.

Dos salvaguardas hacen que las teclas destructivas sean seguras bajo un cuadro de búsqueda. Una combinación solo llega a una acción cuando el cuadro de búsqueda **no tiene texto seleccionado**, así que `Ctrl+X` / `Ctrl+C` / `Ctrl+V` siguen cortando, copiando y pegando tu consulta como siempre. Una tecla aislada como `Delete` solo llega a una acción cuando el cursor **ya está al final** de la consulta, donde no le queda ningún carácter que borrar; en cualquier otra posición del texto sigue siendo una tecla de escritura. Y las dos acciones de borrado pasan por el `IFileOperation` propio del shell, así que el aviso nativo de "¿mover a la Papelera de reciclaje?" / "¿eliminar definitivamente?" sigue interponiéndose entre la tecla y los archivos. Reasigna o vacía cualquiera de ellas en **Configuración → Atajos de teclado → Acciones de plugins**.

### Lista negra de procesos y omisión en pantalla completa

- **Omisión automática en pantalla completa**: Cuando una aplicación se ejecuta en pantalla completa exclusiva (como juegos 3D o reproductores de vídeo), Lertaro omite automáticamente todos los atajos globales para no interferir.
- **Lista negra de procesos personalizada**: Añade ejecutables en [**Configuración → Atajos de teclado**](./settings/hotkeys-page#lista-negra-de-procesos) (p. ej. `game.exe`) para silenciar los atajos mientras ese proceso esté en primer plano.
