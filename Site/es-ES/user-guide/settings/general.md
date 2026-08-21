# General

Seis pestañas: **Sistema**, **Ventana de búsqueda rápida**, **Ventana de búsqueda completa**, **Vista previa**,
**Navegación rápida** y **Vista previa y miniaturas**.

## Sistema

- **Iniciar Lertaro con Windows** — casilla, lanza Lertaro al iniciar sesión.
- **Buscar actualizaciones automáticamente al iniciar** — casilla.
- **Actualización silenciosa automática al detectar una versión nueva** — casilla, solo habilitada mientras la
  comprobación de arriba está activada; descarga e instala las actualizaciones en segundo plano sin preguntar.
- **Habilitar aceleración por hardware** — casilla, activada por defecto. Desactivarla obliga a la ventana de
  búsqueda rápida a renderizarse por software en lugar de usar Direct3D — esto evita que NVIDIA Advanced Optimus
  se niegue a cambiar de GPU en caliente mientras Lertaro está en ejecución (solo se ve afectada la ventana
  rápida, no toda la aplicación). Requiere reiniciar Lertaro para surtir efecto.
- **Ocultar icono de la bandeja** — casilla, desactivada por defecto. Se aplica de inmediato, sin necesidad de
  reiniciar. El mismo menú que muestra el clic derecho del icono de la bandeja siempre está disponible desde el
  [propio logotipo de la ventana rápida](../hotkeys#icono-del-logotipo-en-el-cuadro-de-busqueda) sin importar este
  ajuste, así que ocultar el icono de la bandeja nunca te deja sin forma de volver a Configuración o a Salir.
- **Habilitar emulación Everything IPC** — casilla, desactivada por defecto. Permite que herramientas de terceros dependientes de Everything (como Directory Opus, Total Commander y Flow Launcher) utilicen de forma transparente el índice en memoria de Lertaro para búsquedas y cálculo de tamaño de carpetas.
- **Activar coincidencia difusa** — casilla, activada por defecto. Con ella activada, un término de búsqueda
  normal coincide con que sus caracteres aparezcan en orden en cualquier parte del nombre; al desactivarla, un
  término normal debe aparecer como subcadena contigua en su lugar (`abc` deja de coincidir con `a-b-c`) — ver
  [Sintaxis de búsqueda](../search-syntax#coincidencia-difusa-predeterminada) para lo que cambia y lo que no. Se
  aplica de inmediato, sin necesidad de reiniciar.
- **Carácter de prefijo global de tokens** — cuadro de texto (máximo 1 carácter, predeterminado `:`). Define el carácter de prefijo global para dividir expresiones de tokens al final del cuadro de búsqueda. Los prefijos de filtrado de rutas y filtros personalizados se pueden configurar de forma independiente en **Configuración → Plugins → CoreExtensions**.
- **Nivel de registro** — desplegable: Error / Warn / Info (predeterminado) / Debug. Controla el nivel de detalle
  en los registros de la App, el Servicio y el Hook (ver [Estado del Servicio](./service-status)).
- **Idioma de interfaz** — desplegable, poblado a partir de cada proveedor de traducción instalado (idiomas
  integrados más cualquiera que añada un plugin).

La selección de tema se trasladó a su propia sección [Apariencia](./appearance) — ver esa página para el selector
de tema y la opción de "seguir el ajuste de claro/oscuro del sistema".

## Ventana de búsqueda rápida

Cubre tanto la apariencia de la barra de búsqueda rápida como la forma en que se priorizan sus resultados de
búsqueda.

**Diseño de la barra de búsqueda** — personaliza el tamaño y la posición en pantalla de la barra de búsqueda
rápida:

- **Ancho de la barra de búsqueda (px)** — rango 300–1200 px, por defecto 570 px.
- **Alto de la barra de búsqueda (px)** — rango 45–120 px, por defecto 60 px. Este número también determina el
  tamaño del icono de la fila de resultado, el tamaño de fuente del nombre/ruta, y la altura de fila
  (`altura / 60`), así que una barra de búsqueda más alta escala con ella toda la lista de resultados, manteniendo
  siempre las mismas proporciones entre icono y texto.
- **Mostrar reloj en el cuadro de búsqueda** — casilla, desactivada por defecto. Mientras el cuadro de búsqueda
  está vacío, sustituye el habitual texto de marcador de posición "Escribe para buscar..." por la fecha actual, el
  día de la semana y la hora. Desaparece en cuanto empiezas a escribir, igual que el marcador de posición al que
  sustituye. Solo en la ventana rápida — la ventana en línea siempre mantiene su marcador de posición normal,
  incluso con esto activado.
- **Reabrir como ventana completa al repetir el atajo** — casilla, desactivada por defecto. Normalmente, pulsar de
  nuevo el atajo de mostrar/ocultar mientras la ventana rápida ya está abierta simplemente la oculta; activar esto
  hace que la segunda pulsación cambie a la ventana completa en su lugar (trasladando la consulta que ya tuvieras
  escrita), en lugar de cerrar nada.
- **Bloquear posición** — casilla, desactivada por defecto. Impide arrastrar la ventana rápida, para que una
  pulsación accidental no la desplace del sitio donde la dejaste. Hacer clic derecho en el logo sigue
  restableciendo su posición, y **Restablecer ajustes de diseño** más abajo también quita el bloqueo junto con
  todo lo demás.
- Botón **Restablecer ajustes de diseño** — restaura los cinco ajustes de arriba a sus valores predeterminados.

Hacer clic derecho en el [logotipo de la ventana rápida](../hotkeys#icono-del-logotipo-en-el-cuadro-de-busqueda)
restablece solo su posición en pantalla (no el tamaño), volviendo a centrarla igual que se centra en el primer
arranque.

**Prioridad de tipo de resultado** — la misma lista con flechas arriba/abajo (o arrastrar para reordenar) usada
para [Navegación rápida](#navegacion-rapida) más abajo: mueve un tipo de resultado (Aplicaciones, Configuración,
Filtros de archivos, los propios elementos buscables de cualquier plugin de terceros, o la entrada integrada
"Archivos") arriba o abajo para que siempre supere en prioridad a los tipos que queden por debajo en los resultados
de la ventana rápida, sin importar cuál haya coincidido en realidad mejor con el texto de la consulta. Historial y
Favoritos siempre aparecen primero y no forman parte de esta lista.

Cada tipo también puede tener su propio **activador** de un solo carácter (opcional, un cuadro de texto por fila):
escribir ese carácter como lo primero de todo en la ventana rápida muestra solo los resultados de ese tipo,
ocultando todo lo demás — ver [Sintaxis de búsqueda](../search-syntax#activador-de-tipo-de-resultado) para ver
ejemplos. Escribir solo el activador sin nada detrás todavía muestra un aviso de "sigue escribiendo para buscar
solo en X" en lugar de "Sin resultados de búsqueda". Elige un carácter con el que nunca empezarías normalmente una
búsqueda real, ya que queda reservado en cuanto se le asigna a un tipo — un carácter de puntuación (por ejemplo,
`;`) funciona de forma más fiable que un simple espacio, ya que un espacio suelto sin nada escrito detrás se trata
igual que un cuadro de búsqueda vacío y no mostrará ese aviso.

## Ventana de búsqueda completa

Define el tamaño predeterminado de la ventana de búsqueda completa/principal (la ventana más grande que obtienes
desde la barra de tareas o el acceso directo del menú Inicio, en contraposición a la ventana emergente rápida —
ver [Primeros pasos](../getting-started#las-tres-ventanas)):

- **Ancho de la ventana (px)** — rango 640–2000 px, por defecto 854 px.
- **Alto de la ventana (px)** — rango 400–1400 px, por defecto 480 px. Los mínimos coinciden con el propio límite
  de redimensionado de la ventana, así que un valor configurado nunca es sobrescrito en silencio por la propia
  ventana.
- Botón **Restablecer ajustes de la ventana de búsqueda**.
- **Permitir solo una ventana de búsqueda completa** — casilla, desactivada por defecto. Al estar desactivada, puedes abrir varias ventanas completas independientes. Al activarla, volver a abrir la ventana completa activa una ya existente en vez de crear otra; una consulta transferida desde **Mostrar más** de la ventana rápida sustituye la consulta de esa ventana.

Arrastrar el borde de la ventana para redimensionarla manualmente se recuerda automáticamente — la próxima vez que
abras la ventana (o abras una nueva), vuelve con el tamaño que dejaste la última vez, y los campos de esta página
se actualizan a juego. Redimensionar mientras está maximizada no sobrescribe el tamaño recordado; solo lo hace
redimensionar en el estado normal (no maximizado).

La lista de resultados de la ventana completa no antepone elementos recuperados de Favoritos ni del Historial. Esos resultados personalizados permanecen en las ventanas rápida y en línea; la ventana completa conserva el orden de relevancia normal de los proveedores de búsqueda activos.

**Orden de columnas de la cuadrícula de resultados** — la misma lista de reordenación usada en otras partes de
Configuración (ver [Favoritos](./favorites)): mueve una columna de la cuadrícula de resultados (Nombre, Ruta,
Fecha de modificación, o cualquier columna aportada por un plugin) a la izquierda o a la derecha. Solo afecta al
diseño de resultados en cuadrícula/tabla — no hay columnas que reordenar en el diseño de lista compacta.

**Orden de filtros de la barra lateral** — el mismo mecanismo, para el orden en que aparecen los grupos de filtro
de la barra lateral (Tipo, Fecha de modificación, y cualquier grupo añadido por un plugin).

Mientras se ejecuta una búsqueda completa, cada filtro de la barra lateral muestra en tiempo real cuántos resultados coinciden con la consulta actual. El recuento se actualiza a medida que llegan resultados y se reinicia al cambiar la consulta; no depende del filtro de la barra lateral seleccionado. El ancho de la barra lateral se ajusta al nombre y recuento más largos para que los nombres se muestren completos sin saltos de línea.

**Orden de secciones del menú de acciones** — el mismo mecanismo, para el orden en que aparecen las secciones del
[menú de acciones](../actions-and-preview#menu-de-acciones): el grupo de acciones integradas, más una sección por
cada plugin que contribuya acciones ahí (por ejemplo, Acciones personalizadas, o el menú contextual del shell de
Windows). Una sección que aún no esté en esta lista recurre a su posición natural (primero las integradas, luego
las secciones de plugin en el orden en que se aportaron).

Hacer clic en la cabecera de una columna de la cuadrícula de resultados recorre tres estados: ascendente,
descendente, y un tercer clic la restablece de vuelta al orden predeterminado por relevancia (la flecha de orden de
la cabecera desaparece). Esto se recuerda solo mientras Lertaro sigue en ejecución — salir de la aplicación (no
solo cerrar la ventana) restablece el orden al predeterminado la próxima vez que la abras, a diferencia del orden
de columnas/barra lateral de arriba, que se guarda de forma permanente.

## Vista previa

- **Ancho de la vista previa (px)** — rango 250–900 px.
- **Alto de la vista previa (px)** — rango 250–1200 px, con un tamaño predeterminado pensado para que el panel no
  sea excesivamente alto en una pantalla típica.
- Botón **Restablecer ajustes de la ventana de vista previa**.

La ventana de vista previa ignora el número actual de resultados — es un tamaño fijo, no un tamaño que crece con
el contenido. Ver [Menú de acciones y vista previa](../actions-and-preview) para saber cómo se posiciona el panel.

## Navegación rápida

Define el orden en que aparecen las secciones de nivel raíz del menú de [Navegación
rápida](../hotkeys#navegacion-rapida-raton) — una sección por cada proveedor que contribuye (por ejemplo,
Favoritos/Historial/carpetas configuradas, la lista de carpetas favoritas de Total Commander, los Favoritos de
Directory Opus, las propias entradas de navegación rápida de un plugin), cada una etiquetada con su propio
encabezado.

**Orden de proveedores** — la misma lista con flechas arriba/abajo (o arrastrar para reordenar) usada en otras
partes de Configuración (ver [Favoritos](./favorites)): mueve un proveedor arriba o abajo para cambiar dónde cae
su sección respecto a las demás. Solo se listan aquí los proveedores cuyo componente de plugin esté actualmente
habilitado — uno deshabilitado en [Plugins](./plugins) nunca llega a ser candidato al menú, así que no hay nada
que ordenar para él.

## Vista previa y miniaturas

Dos listas de prioridad independientes, cada una decidiendo qué proveedor tiene la primera opción de rechazo para
su propia tarea — normalmente decidido puramente por la prioridad propia, fija e integrada, de cada proveedor, sin
forma de cambiar eso salvo deshabilitando un proveedor por completo.

**Orden de proveedores de vista previa de archivo** — para los [proveedores de vista previa de
archivo](../actions-and-preview#vista-previa-quicklook) (por ejemplo, el plugin [QuickLook
Bridge](../actions-and-preview#vista-previa-externa-mediante-quicklook-opcional) establece su propia prioridad por
encima de cualquier previsualizador integrado). La misma lista con flechas arriba/abajo (o arrastrar para
reordenar) usada en otras partes de Configuración (ver [Favoritos](./favorites)): mueve un proveedor hacia arriba
para que siempre gane sobre los que quedan por debajo, sin importar su propia prioridad integrada. Solo se listan
aquí los proveedores cuyo componente de plugin esté actualmente habilitado — uno deshabilitado en
[Plugins](./plugins) de todos modos nunca llegaría a previsualizar nada.

**Orden de proveedores de miniaturas** — el mismo mecanismo, para los iconos/miniaturas mostrados junto a los
resultados en la propia lista de búsqueda (a diferencia del panel de vista previa) — más relevante en cuanto más
de un plugin implemente un proveedor de miniaturas personalizado, ya que hoy en día solo existe un proveedor
integrado.
