# Configuración general

Configuración general abarca el comportamiento central del sistema, las dimensiones y el diseño visual de las ventanas de búsqueda, la ponderación de prioridad de los tipos de resultados y el orden de los proveedores de vista previa. La página se divide en seis pestañas superiores: **Sistema**, **Ventana de búsqueda rápida**, **Ventana de búsqueda principal**, **Ventana de vista previa**, **Navegación rápida** y **Vista previa y miniaturas**.

## 1. Sistema

- **Iniciar Lertaro al arrancar el sistema**: Inicia Lertaro automáticamente al iniciar sesión en Windows.
- **Buscar actualizaciones al iniciar**: Comprueba automáticamente en línea si hay nuevas versiones al abrir la aplicación.
- **Actualizaciones silenciosas**: Solo disponible cuando se ejecuta como administrador y "Buscar actualizaciones" está activo. Descarga e instala actualizaciones en segundo plano sin cuadros de diálogo molestos.
- **Habilitar aceleración por hardware**: Activada por defecto para Ajustes, Búsqueda completa y Vista previa. La búsqueda rápida, las tarjetas integradas y las notificaciones siempre usan renderizado por software para reducir la demora inicial. Desactívala si Lertaro impide volver a la gráfica integrada de un portátil con dos GPU. Requiere reiniciar.
- **Ocultar icono de la bandeja del sistema**: Oculta el icono del área de notificación. El logotipo dentro de la barra de búsqueda rápida sigue desplegando el menú completo de la bandeja.
- **Habilitar servicio de compatibilidad Everything (IPC)**: Emula el protocolo Win32 IPC de Everything para que Directory Opus, Total Commander y otras herramientas consulten el índice en memoria de Lertaro directamente.
- **Habilitar coincidencia difusa**: Activado por defecto. Permite coincidencias no consecutivas; si se desactiva, solo coincidirán subcadenas continuas (ver [**Sintaxis de búsqueda**](../search-syntax)). Surte efecto inmediato.
- **Mantener el contenido del cuadro de búsqueda al cerrar**: Desactivada por defecto. Conserva la consulta al cerrar la búsqueda rápida o pulsar `Esc` en la ventana completa, que se cierra con una sola pulsación. Al reabrir, el texto queda **seleccionado por completo** para sustituirlo al escribir. Solo se conserva durante esta sesión.
- **Mostrar las carpetas abiertas actualmente en la búsqueda integrada**: Cuando la ventana integrada está acoplada a un cuadro de diálogo de archivos, muestra las carpetas abiertas actualmente en ese diálogo cuando la consulta está vacía. Está activado de forma predeterminada.
- **Ancho de la Ventana Integrada (%)**: Admite `20`–`100`. Salvo el valor predeterminado `60`, indica el porcentaje del ancho de la ventana anfitriona. `60` activa el tamaño automático: el mayor entre el 40% del anfitrión y un quinto de la pantalla. El mínimo es el mayor entre el 20% del anfitrión y un sexto de la pantalla; el máximo, el menor entre el ancho del anfitrión y el 90% de la pantalla.
- **Filas de la Ventana Integrada (Ventana Normal)**: Muestra de 4 a 10 resultados al acoplarse a una ventana normal (10 de forma predeterminada). Los encabezados no cuentan. Ctrl+1–9 selecciona los resultados 1–9 y Ctrl+0 el décimo. Desplázate para ver las filas que no quepan.
- **Filas de la Ventana Integrada (Selector de Archivos)**: Muestra de `4` a `10` resultados en diálogos Abrir, Guardar o Examinar; el valor predeterminado es `4`. Los encabezados, atajos y límites de altura funcionan como se indica arriba.
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
- **Mostrar la barra de filtros por tipo**: Activado por defecto. Muestra las pestañas de tipo bajo la búsqueda rápida. Al desactivarlas, el filtro vuelve a Todo y queda más espacio para los resultados.
- **Recordar la última pestaña de la barra de filtros por tipo**: Desactivado por defecto. Conserva la pestaña de tipo al reabrir la búsqueda rápida. Vuelve a Todo al reiniciar Lertaro.
- **Restablecer diseño**: Restaura todas las propiedades de diseño a sus valores predeterminados.

### Prioridad de tipos de resultado y activadores

- **Lista de ordenación de prioridades**: Arrastra Aplicaciones, Ajustes del sistema, Archivos y Carpetas al orden deseado. Desempata después del grado, la prioridad, el uso y la posición. Poner aplicaciones o ajustes antes de archivos y carpetas hace que los nombres exactos de estos últimos compitan como coincidencias fuertes. Consulta [Sintaxis de búsqueda](../search-syntax).
- **Carácter activador exclusivo**: Asigna un prefijo de un solo carácter (p. ej. `;` para Filtros de archivos) para aislar las búsquedas a ese tipo concreto.
- **Exportación de detalles de ordenación**: En la ventana rápida, haz clic derecho en cualquier resultado y elige **Exportar los detalles de ordenación de esta página** para escribir un archivo de texto de toda la página mostrada. El ajuste **Permitir exportar los detalles de ordenación** está activado por defecto.

## 3. Ventana de búsqueda principal

Configura las dimensiones predeterminadas y la vista de tabla para la ventana principal (`Ctrl+F`):

- **Ancho / Alto de la ventana (Píxeles)**: Ancho `640–2000px` (por defecto `854px`), alto `400–1400px` (por defecto `480px`).
- **Permitir solo una ventana principal**: Al volver a abrir la ventana principal, enfoca la existente en lugar de duplicarla.
- **Cerrar al repetir el atajo**: Desactivado por defecto, pulsar de nuevo el atajo global con la ventana completa en primer plano vuelve a la ventana de búsqueda rápida manteniendo la consulta actual, alternando entre ambas ventanas hasta pulsar `Esc`. Si se activa, al pulsarlo de nuevo se cierra directamente la ventana completa. En ambos casos, si la ventana completa está visible pero sin foco, el atajo solo la trae de vuelta al frente.
- **Restablecer configuración de ventana**: Vuelve a los tamaños predeterminados de fábrica.
- **Orden de columnas en la tabla**: Personaliza el orden de las columnas (Nombre, Ruta, Fecha de modificación, etc.).
- **Orden de filtros en la barra lateral**: Reordena los filtros de la barra lateral; cada uno muestra en tiempo real el recuento de coincidencias activas.
- **Orden de grupos del menú de acciones**: Modifica la disposición de los grupos dentro del menú de acciones contextuales (`Ctrl+O`).

## 4. Ventana de vista previa

- **Ancho / Alto de la vista previa (Píxeles)**: Ancho `250–900px`, alto `250–1200px`.
- **Restablecer vista previa**: Devuelve el panel de QuickLook a sus proporciones estándar, manteniéndose siempre dentro de los límites visibles de la pantalla.

## 5. Navegación rápida

- **Orden de proveedores**: Reordena los grupos de proveedores del nivel superior en el menú de Navegación rápida. Cada fila es un proveedor, por ejemplo Cascada de Carpetas y Ejecutor de Comandos del Sistema, además de los grupos de marcadores de los complementos de exploradores.

## 6. Vista previa y miniaturas

- **Orden de proveedores de vista previa**: Ajusta la precedencia entre los renderizadores multimedia internos y el puente externo de QuickLook.
- **Orden de proveedores de miniaturas**: Ajusta la prioridad de los componentes encargados de extraer iconos y miniaturas de archivos.
