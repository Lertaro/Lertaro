# Configuración general

Configuración general abarca el comportamiento central del sistema, las dimensiones y el diseño visual de las ventanas de búsqueda, la ponderación de prioridad de los tipos de resultados y el orden de los proveedores de vista previa. La página se divide en seis pestañas superiores: **Sistema**, **Ventana de búsqueda rápida**, **Ventana de búsqueda principal**, **Ventana de vista previa**, **Navegación rápida** y **Vista previa y miniaturas**.

## 1. Sistema

- **Iniciar Lertaro al arrancar el sistema**: Inicia Lertaro automáticamente al iniciar sesión en Windows.
- **Buscar actualizaciones al iniciar**: Comprueba automáticamente en línea si hay nuevas versiones al abrir la aplicación.
- **Actualizaciones silenciosas**: Solo disponible cuando se ejecuta como administrador y "Buscar actualizaciones" está activo. Descarga e instala actualizaciones en segundo plano sin cuadros de diálogo molestos.
- **Habilitar aceleración por hardware**: Activado por defecto. Se aplica a las ventanas que mantienes abiertas (Ajustes, la ventana completa de búsqueda y la vista previa); las ventanas que invocas y cierras (búsqueda rápida, tarjeta en línea y notificaciones) se dibujan siempre por software, que es lo que hace que aparezcan ya pintadas en lugar de como un marco vacío. Si tu portátil con doble GPU (como NVIDIA Advanced Optimus) no cambia de gráfica por la presencia de Lertaro, desactívalo para renderizar todas las ventanas por software. Requiere reiniciar Lertaro.
- **Ocultar icono de la bandeja del sistema**: Oculta el icono del área de notificación. El logotipo dentro de la barra de búsqueda rápida sigue desplegando el menú completo de la bandeja.
- **Habilitar servicio de compatibilidad Everything (IPC)**: Emula el protocolo Win32 IPC de Everything para que Directory Opus, Total Commander y otras herramientas consulten el índice en memoria de Lertaro directamente.
- **Habilitar coincidencia difusa**: Activado por defecto. Permite coincidencias no consecutivas; si se desactiva, solo coincidirán subcadenas continuas (ver [**Sintaxis de búsqueda**](../search-syntax)). Surte efecto inmediato.
- **Mantener el contenido del cuadro de búsqueda al cerrar**: Desactivado por defecto. Cuando está activo, ni ocultar la Ventana rápida ni pulsar `Escape` en la Ventana principal vacían el cuadro; `Escape` en la Ventana principal cierra la ventana a la primera pulsación en lugar de pedirlo dos veces. La consulta que recibe una ventana en su siguiente invocación llega **seleccionada completa**, así que la primera tecla la sustituye en lugar de añadirse. Esto solo dura la ejecución actual; el texto nunca se escribe en disco.
- **Mostrar las carpetas abiertas actualmente en la búsqueda integrada**: Cuando la ventana integrada está acoplada a un cuadro de diálogo de archivos, muestra las carpetas abiertas actualmente en ese diálogo cuando la consulta está vacía. Está activado de forma predeterminada.
- **Ancho de la Ventana Integrada (%)**: Cuadro de texto que acepta de `20` a `100`, por defecto `60`. La ventana integrada toma ese porcentaje del ancho de la ventana a la que se acopla. Guardar `60` no deja ninguna elección registrada, así que una tarjeta intacta toma el mayor entre el 40 % de esa ventana y una quinta parte del ancho de la pantalla. La pantalla sigue poniendo límites: nunca por debajo del mayor entre el 20 % de esa ventana y una sexta parte del ancho de la pantalla, y nunca por encima del menor entre esa ventana misma y el 90 % del ancho de la pantalla. Redúcelo si la tarjeta tapa demasiado una ventana maximizada.
- **Filas de la Ventana Integrada (Ventana Normal)**: Cuadro de texto que acepta de `4` a `10`, por defecto `10`. Cuántos resultados muestra la ventana integrada acoplada a una ventana normal. Los títulos de sección no cuentan: ocupan filas propias además del número que fijes. `Ctrl+1`–`Ctrl+9` seleccionan el primero al noveno resultado y `Ctrl+0` el décimo, de ahí el límite de 10. Si la pantalla no es lo bastante alta se muestran las filas que caben con desplazamiento.
- **Filas de la Ventana Integrada (Selector de Archivos)**: El mismo rango, por defecto `4`. Es el número de la tarjeta acoplada sobre un selector de archivos (Abrir/Guardar/Examinar); un selector es en gran parte controles de otro programa, así que esa tarjeta empieza más baja. Títulos, atajos y límite de pantalla funcionan igual que arriba.
- **Delimitador de tokens de consulta**: Campo de un solo carácter (por defecto `:`). Define el prefijo para los tokens de sufijo (p. ej. `:.pdf`, `:@doc`, `:[S]`).
- **Nivel de registro**: Selecciona Error / Advertencia / Información (predeterminado) / Depuración para la verbosidad de los registros.
- **Idioma de la interfaz**: Selecciona el idioma global de la aplicación.

## 2. Ventana de búsqueda rápida

Permite ajustar con precisión las dimensiones geométricas y las prioridades de la barra flotante centrada:

### Diseño de la barra de búsqueda

- **Ancho de la barra (Píxeles)**: Rango `300–1200px`, por defecto `570px`.
- **Alto de la barra (Píxeles)**: Rango `45–120px`, por defecto `60px`. Este valor escala proporcionalmente el tamaño de los iconos, el alto de fila y la tipografía para mantener la armonía visual.
- **Mostrar reloj en la barra de búsqueda**: Sustituye el texto de sugerencia por la fecha y hora actuales cuando la barra está vacía. Desaparece al empezar a escribir.
- **Abrir ventana principal al pulsar de nuevo el atajo**: Si la Ventana rápida ya está abierta, pulsar de nuevo su atajo transfiere la búsqueda activa a la Ventana principal.
- **Bloquear posición**: Evita que la barra se mueva accidentalmente al arrastrarla con el ratón.
- **Rellenar automáticamente el texto del portapapeles**: Desactivado por defecto. Cuando la Ventana de búsqueda rápida se abre sin texto preintroducido, usa como consulta el texto nuevo y no vacío del portapapeles. El pegado manual y las demás ventanas no se ven afectados.
- **Restablecer diseño**: Restaura todas las propiedades de diseño a sus valores predeterminados.

### Prioridad de tipos de resultado y activadores

- **Lista de ordenación de prioridades**: Arrastra los controles para decidir qué tipos van primero **entre coincidencias directas con relevancia similar**, sin superar nunca a coincidencias directas exactas.
- **Carácter activador exclusivo**: Asigna un prefijo de un solo carácter (p. ej. `;` para Filtros de archivos) para aislar las búsquedas a ese tipo concreto.
- **Exportación de detalles de ordenación**: En la ventana rápida, haz clic derecho en cualquier resultado y elige **Exportar los detalles de ordenación de esta página** para escribir un archivo de texto de toda la página mostrada. El ajuste **Permitir exportar los detalles de ordenación** está activado por defecto.

## 3. Ventana de búsqueda principal

Configura las dimensiones predeterminadas y la vista de tabla para la ventana principal (`Ctrl+F`):

- **Ancho / Alto de la ventana (Píxeles)**: Ancho `640–2000px` (por defecto `854px`), alto `400–1400px` (por defecto `480px`).
- **Permitir solo una ventana principal**: Al volver a abrir la ventana principal, enfoca la existente en lugar de duplicarla.
- **Cerrar al repetir el atajo**: Desactivado por defecto, pulsar de nuevo el atajo global con la ventana completa en primer plano vuelve a la ventana de búsqueda rápida manteniendo la consulta actual, alternando entre ambas ventanas hasta pulsar `Esc`. Si se activa, al pulsarlo de nuevo se cierra directamente la ventana completa. En ambos casos, si la ventana completa está visible pero sin foco, el atajo solo la trae de vuelta al frente.
- **Restablecer configuración de ventana**: Vuelve a los tamaños predeterminados de fábrica.
- **Orden de columnas en la tabla**: Personaliza el orden de las columnas (Nombre, Ruta, Fecha de modificación, etc.).
- **Orden de filtros en la barra lateral**: Reordena las categorías laterales; cada una muestra en tiempo real el recuento de coincidencias activas.
- **Orden de grupos del menú de acciones**: Modifica la disposición de los grupos dentro del menú de acciones contextuales (`Ctrl+O`).

## 4. Ventana de vista previa

- **Ancho / Alto de la vista previa (Píxeles)**: Ancho `250–900px`, alto `250–1200px`.
- **Restablecer vista previa**: Devuelve el panel de QuickLook a sus proporciones estándar, manteniéndose siempre dentro de los límites visibles de la pantalla.

## 5. Navegación rápida

- **Orden de proveedores**: Reordena los grupos de proveedores del nivel superior en el menú de Navegación rápida. Cada fila es un proveedor, por ejemplo Cascada de Carpetas y Ejecutor de Comandos del Sistema, además de los grupos de marcadores de los complementos de exploradores.

## 6. Vista previa y miniaturas

- **Orden de proveedores de vista previa**: Ajusta la precedencia entre los renderizadores multimedia internos y el puente externo de QuickLook.
- **Orden de proveedores de miniaturas**: Ajusta la prioridad de los componentes encargados de extraer iconos y miniaturas de archivos.
