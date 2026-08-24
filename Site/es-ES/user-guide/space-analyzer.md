# Analizador de espacio

Lertaro incluye un **Analizador de espacio (Space Analyzer)** ultrarrápido para discos y carpetas. A diferencia de las herramientas tradicionales que requieren escaneos físicos completos de los sectores, aprovecha el árbol de índices en memoria de Lertaro para desglosar el almacenamiento en milisegundos, incluso en unidades con millones de archivos.

## 1. Acceso al Analizador de espacio

- **Aparición automática**: Abre la Ventana principal de búsqueda (`Ctrl+F`) dejando la **barra de búsqueda vacía**; el Analizador de espacio se mostrará automáticamente como vista principal.
- **Transición fluida**: Escribir cualquier carácter cambia al instante a la lista de resultados de búsqueda; borrar la búsqueda regresa de inmediato a la vista del Analizador de espacio.

## 2. Diseño y visualización

El Analizador de espacio utiliza una disposición en dos paneles sincronizados para ofrecer máxima claridad:

### Panel izquierdo: Gráfico Treemap

- **Área proporcional**: Los rectángulos más grandes corresponden a carpetas o archivos que ocupan mayor volumen de almacenamiento.
- **Profundidad de color y bordes**: El sombreado refleja el peso relativo dentro del directorio actual, con bordes diferenciados para distinguir carpetas de archivos individuales.

### Panel derecho: Lista ordenada por tamaño

- **Orden descendente**: Lista los elementos ordenados de mayor a menor, identificando al instante los elementos que más espacio consumen.
- **Barra de porcentaje**: Cada fila incluye una sutil barra de progreso que indica su proporción respecto al total visible.
- **Resaltado bidireccional**: Al seleccionar un elemento en el Treemap o en la lista derecha, se enfoca simultáneamente en ambos paneles.
- **Desplazamiento fluido de nombres largos**: Los nombres que superen el ancho visible se desplazan suavemente al pasar el ratón o seleccionarlos.

## 3. Navegación y operaciones contextuales

- **Exploración jerárquica**:
  - **Entrar en una carpeta**: Haz doble clic izquierdo en cualquier tarjeta del Treemap o fila de la lista para explorar su interior.
  - **Subir de nivel**: Haz clic en la flecha "Arriba" en la barra de navegación o en cualquier carpeta de la ruta de navegación (Breadcrumbs).
- **Menú de acciones contextual**: Haz clic derecho en cualquier tarjeta o fila para abrir el **Menú de acciones** estándar (Abrir, Copiar ruta completa, Mostrar en Explorador, Enviar a la papelera o Eliminar permanentemente).
- **Ubicar con clic central**: Haz clic central en cualquier elemento para abrir y ubicar su posición en tu explorador de archivos configurado.
- **Vista previa sincronizada**: Pulsa `Alt+P` para abrir el panel de QuickLook; la vista previa se actualiza dinámicamente conforme navegas por los elementos.

## 4. Criterios de cálculo y sincronización en tiempo real

### Ámbito y cálculo de tamaños

- **Basado en el índice existente**: Solo resume los elementos indexados por Lertaro sin realizar operaciones de E/S adicionales en disco. Los archivos excluidos por reglas no se contabilizan.
- **Tamaño lógico de archivos**: Muestra los tamaños lógicos reales; los datos con enlaces físicos (hard links) solo se computan una vez para evitar duplicidades.
- **Archivos ocultos y del sistema**: Los archivos ocultos se incluyen con normalidad; los archivos del sistema se consolidan en el tamaño total de su carpeta superior.

### Seguimiento de cambios y autorrecuperación

- **Actualizaciones en vivo**: Recibe notificaciones de cambios del servicio de indexación y actualiza la vista de forma fluida.
- **Recuperación automática de rutas**: Si la carpeta activa es eliminada o renombrada externamente, el Analizador de espacio retrocede de forma inteligente a la carpeta superior válida más cercana sin bloquearse.
