# Panel rápido (Configuración)

El **Panel rápido (Quick Panel)** es un espacio de trabajo flotante diseñado para la consulta rápida de archivos, la gestión del contexto de proyectos y el depósito de archivos mediante arrastrar y soltar. Al invocarse, se acopla automáticamente a la esquina inferior derecha de la ventana activa, permitiéndote acceder y preparar archivos sin cambiar de contexto.

## 1. Mecanismo central e interruptor general

- **Habilitar Panel rápido**: Interruptor maestro; cuando está desactivado, el atajo deja de interceptar pulsaciones.
- **Atajo de invocación**: Por defecto **`Ctrl+F2`** (personalizable en [**Configuración → Atajos de teclado**](./hotkeys-page)).
- **Acoplamiento inteligente y dimensiones**: Se ajusta por defecto a la mitad del ancho y alto de la ventana anfitriona (con un tamaño mínimo de `280 × 200px` para asegurar la legibilidad). Si una ventana de Lertaro ya tiene el foco, la invocación se ignora para no apilar ventanas; si el panel ya está abierto, el mismo atajo lo cierra.
- **Integración con Salto rápido**: Mientras el panel está abierto, la carpeta física del grupo seleccionado se registra como directorio de trabajo. Al pulsar Salto rápido (`Ctrl+G`) en cuadros de diálogo de archivos, se navegará directamente hasta ella.

## 2. Espacios de trabajo (Workspaces)

Permiten organizar carpetas por proyecto o tarea:

- **Gestión de espacios de trabajo**: La lista izquierda permite **Crear**, **Duplicar** y **Eliminar**, con ordenación por arrastre que se refleja en la barra de pestañas.
- **Propiedades**:
  - **Nombre**: Texto de la pestaña (usa el nombre predeterminado localizado si se deja en blanco).
  - **Interruptor de activación**: Controla la presencia de la pestaña en la barra. Pulsar **×** en una pestaña del panel la oculta; se puede volver a activar aquí.

Los espacios de trabajo seleccionados se configuran en dos pestañas: **Orígenes** y **Aplicaciones**.

## 3. Configuración de orígenes (Sources)

Cada origen representa un grupo independiente dentro del espacio de trabajo:

- **Añadir carpeta**: Selecciona el directorio en disco.
- **Modo de visualización**:
  - **Archivos modificados recientemente** —— Consulta el índice en memoria en submilisegundos para mostrar cambios recientes (los más nuevos primero).
  - **Todos los archivos, más recientes primero** —— Muestra todos los archivos ordenados por fecha de modificación descendente.
  - **Todos los archivos, por nombre** —— Funciona como una barra fija de accesos directos.
- **Incluir subcarpetas**: Incluye archivos descendientes de forma recursiva.
- **Aceptar archivos arrastrados**: Permite arrastrar archivos, carpetas o imágenes web desde otras ventanas. Lertaro ejecuta una copia nativa de Windows con avisos de conflicto y opción de deshacer.
- **Reglas de filtrado**: Filtra las extensiones visibles mediante comodines o filtros `@` de la sintaxis de búsqueda (p. ej. `*.mp4;*.mkv`, `*.lnk;:@doc;:@img` o `*.lnk;:@doc|img`).
- **Límite de elementos y tiempo**: Restringe la cantidad máxima visible (0 para ilimitado) o muestra solo archivos modificados en los últimos N minutos.
- **Lista detallada frente a mosaico de miniaturas**: Escoge entre vista de lista compacta o cuadrícula de miniaturas (las miniaturas escalan proporcionalmente conservando su formato original).

## 4. Pestañas de plugins (Plugin Tabs)

Los plugins pueden registrar listas dinámicas globales en el Panel rápido. El plugin CoreExtensions incluye cinco pestañas predefinidas:

| Pestaña de plugin | Contenido |
| :--- | :--- |
| **Favoritos** | Elementos destacados con estrella; las URLs se abren en el navegador predeterminado. |
| **Historial** | Elementos y aplicaciones ejecutados recientemente mediante Lertaro. |
| **Historial de Windows** | Resuelve los Documentos recientes de Windows en rutas físicas reales. |
| **Última carpeta** | Rastrea la carpeta recién visitada en el Explorador o en cuadros de diálogo. |
| **Archivos recientes** | Agrupa los archivos más nuevos de todas las carpetas configuradas usando el índice. |

Cada pestaña de plugin se puede activar/desactivar y configurar en modo Lista o Mosaico.

## 5. Vinculación con aplicaciones y lista negra

- **Aplicaciones**: Asocia nombres de procesos (p. ej. `chrome.exe` o `devenv.exe`) al espacio de trabajo. Al invocar el panel sobre estas aplicaciones, se abrirá directamente el espacio de trabajo correspondiente.
- **Lista negra exclusiva del Panel rápido**: Lista de procesos sobre los que no debe aparecer el panel. Se **suma** a la lista negra global de atajos.

## 6. Guía de uso e interacción

- **Filtrado difuso en vivo**: La barra de búsqueda superior derecha admite coincidencia difusa fzf y alias de pinyin para filtrar el espacio de trabajo activo. Filtra **solo nombres** y nunca reordena una lista; un grupo cuyos elementos desaparecen todos se oculta, mientras su pestaña permanece. `Escape` borra primero el filtro y solo un **segundo** `Escape` cierra el panel.
- **Navegación fluida por teclado**: Las flechas de dirección navegan fluidamente entre grupos; pulsa `Enter` para abrir el elemento resaltado y el panel sigue abierto. La secuencia **no** da la vuelta: el primer y el último elemento visibles son extremos definitivos, los grupos vacíos se saltan, y sin nada seleccionado todavía tanto `↑` como `↓` caen en el primer elemento. En la vista de mosaico las cuatro flechas se mueven por la cuadrícula visual: `←`/`→` siguen el orden de lectura y atraviesan los límites de las filas, `↑`/`↓` conservan la columna.
- **Cambio de pestañas**: Pulsa `Ctrl` + `1`–`9` para alternar rápidamente entre pestañas de espacios de trabajo y plugins.
- **QuickLook y fijación**: Pulsa `Alt+P` para abrir la vista previa acoplada; pulsa `Ctrl+T` — o el botón de fijación del encabezado — para fijar el panel y evitar que se cierre al perder el foco.
- **Dónde recae el foco**: Al invocar el panel el cursor queda en el cuadro de filtro con la primera entrada ya seleccionada, así que escribir reduce la lista de inmediato. Mientras ese cuadro tiene el foco, los atajos de acciones de los plugins se apartan a propósito — `Ctrl+A` y `Ctrl+C` significan edición de texto aquí, no operaciones de archivos.
- **Panel de acciones flotante**: La **soltura** del clic derecho (no su pulsación) abre el mismo panel de acciones flotante que muestra la Ventana principal, anclado al cursor, por lo que un clic derecho en una fila no seleccionada actúa sobre esa fila. El panel pasa entonces a hacerse con `↑ ↓ ← →`, `Enter`, `Space` y `Escape`. Como cuelga su gestor de teclas del propio panel, se ignora el clic fuera que, de otro modo, cerraría el panel mientras esté abierto.
- **El ratón sobre las filas**: El doble clic izquierdo abre; un doble clic **derecho** solo vuelve a abrir el panel y nunca inicia nada. El clic central alterna la vista previa. Pasar el ratón por encima de una fila mueve la selección.
- **Mover el panel**: Aparece una barra de arrastre en el encabezado cuando el puntero se acerca; arrastrar ahí mueve el panel, y el panel sobrevive al arrastre (por eso un archivo arrastrado desde el Explorador puede acabar dentro).
- **Clic fuera**: El panel se oculta unos 200 ms después de que el foco se vaya, y no actúa cuando lo estabas arrastrando, cuando está fijado, mientras el panel de acciones flotante esté abierto y mientras la vista previa que abrió tenga el primer plano — hacer clic en tu propia vista previa no es un clic fuera.
- **Edición de la selección**: Hacer clic en el espacio en blanco dentro de un grupo limpia la selección de **todos** los grupos a la vez (salvo que se mantenga `Ctrl` o `Shift`). Un arrastre rectangular selecciona dentro de su propio grupo, y con `Ctrl` de forma acumulativa; un rectángulo que empieza en un grupo no alcanza a otro.
- **Soltar archivos dentro**: Un arrastre que **empezó dentro de Lertaro** nunca se acepta, así que un arrastre a medio terminar no puede convertirse en una copia indeseada. Como el panel se cierra al hacer clic fuera, arrastrar un archivo desde el Explorador solo funciona con el panel **fijado**. Soltar archivos siempre produce una copia, nunca un movimiento.
- **Encabezados de grupo**: Hacer clic en el nombre, la ruta o la línea de reglas del encabezado pliega y despliega el grupo; Ordenar y Vista son botones separados a su lado. La ordenación, el modo de vista y el estado de plegado son de la sesión, mientras que el orden de las pestañas y el cierre de una pestaña se guardan.
