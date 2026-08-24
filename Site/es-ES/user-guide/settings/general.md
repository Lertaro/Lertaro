# Configuración general

Configuración general abarca el comportamiento central del sistema, las dimensiones y el diseño visual de las ventanas de búsqueda, la ponderación de prioridad de los tipos de resultados y el orden de los proveedores de vista previa. La página se divide en seis pestañas superiores: **Sistema**, **Ventana de búsqueda rápida**, **Ventana de búsqueda principal**, **Ventana de vista previa**, **Navegación rápida** y **Vista previa y miniaturas**.

## 1. Sistema

- **Iniciar Lertaro al arrancar el sistema**: Inicia Lertaro automáticamente al iniciar sesión en Windows.
- **Buscar actualizaciones al iniciar**: Comprueba automáticamente en línea si hay nuevas versiones al abrir la aplicación.
- **Actualizaciones silenciosas**: Solo disponible cuando "Buscar actualizaciones" está activo. Descarga e instala actualizaciones en segundo plano sin cuadros de diálogo molestos.
- **Habilitar aceleración por hardware**: Activado por defecto. Si tu portátil con doble GPU (como NVIDIA Advanced Optimus) no cambia de gráfica por la presencia de Lertaro, desactívalo para usar renderizado por software. Requiere reiniciar Lertaro.
- **Ocultar icono de la bandeja del sistema**: Oculta el icono del área de notificación. El logotipo dentro de la barra de búsqueda rápida sigue desplegando el menú completo de la bandeja.
- **Habilitar servicio de compatibilidad Everything (IPC)**: Emula el protocolo Win32 IPC de Everything para que Directory Opus, Total Commander y otras herramientas consulten el índice en memoria de Lertaro directamente.
- **Habilitar coincidencia difusa**: Activado por defecto. Permite coincidencias no consecutivas; si se desactiva, solo coincidirán subcadenas continuas (ver [**Sintaxis de búsqueda**](../search-syntax)). Surte efecto inmediato.
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
- **Restablecer diseño**: Restaura todas las propiedades de diseño a sus valores predeterminados.

### Prioridad de tipos de resultado y activadores

- **Lista de ordenación de prioridades**: Arrastra los controles para cambiar la precedencia de aplicaciones, ajustes, archivos y extensiones de plugins.
- **Carácter activador exclusivo**: Asigna un prefijo de un solo carácter (p. ej. `;` para Filtros de archivos) para aislar las búsquedas a ese tipo concreto.

## 3. Ventana de búsqueda principal

Configura las dimensiones predeterminadas y la vista de tabla para la ventana principal (`Ctrl+F`):

- **Ancho / Alto de la ventana (Píxeles)**: Ancho `640–2000px` (por defecto `854px`), alto `400–1400px` (por defecto `480px`).
- **Permitir solo una ventana principal**: Al volver a abrir la ventana principal, enfoca la existente en lugar de duplicarla.
- **Restablecer configuración de ventana**: Vuelve a los tamaños predeterminados de fábrica.
- **Orden de columnas en la tabla**: Personaliza el orden de las columnas (Nombre, Ruta, Fecha de modificación, etc.).
- **Orden de filtros en la barra lateral**: Reordena las categorías laterales; cada una muestra en tiempo real el recuento de coincidencias activas.
- **Orden de grupos del menú de acciones**: Modifica la disposición de los grupos dentro del menú de acciones contextuales (`Ctrl+O`).

## 4. Ventana de vista previa

- **Ancho / Alto de la vista previa (Píxeles)**: Ancho `250–900px`, alto `250–1200px`.
- **Restablecer vista previa**: Devuelve el panel de QuickLook a sus proporciones estándar, manteniéndose siempre dentro de los límites visibles de la pantalla.

## 5. Navegación rápida

- **Orden de proveedores**: Reordena las secciones de nivel superior en el menú de Navegación rápida (Favoritos, Historial, Carpetas abiertas y marcadores de exploradores).

## 6. Vista previa y miniaturas

- **Orden de proveedores de vista previa**: Ajusta la precedencia entre los renderizadores multimedia internos y el puente externo de QuickLook.
- **Orden de proveedores de miniaturas**: Ajusta la prioridad de los componentes encargados de extraer iconos y miniaturas de archivos.
