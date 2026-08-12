# Analizador de espacio

El Analizador de espacio convierte los índices de archivos existentes de Lertaro en una vista rápida similar a SpaceSniffer. No analiza los discos, por lo que se abre con rapidez incluso si varias unidades contienen millones de elementos.

## Abrir el analizador

Abre la ventana de búsqueda completa de Lertaro y deja vacío el cuadro de búsqueda. El Analizador de espacio aparece automáticamente como página de inicio y muestra todos los índices cargados actualmente por Lertaro. Al escribir una consulta se cambia de inmediato a los resultados de búsqueda; al borrarla se vuelve a la raíz del analizador. Haz doble clic con el botón izquierdo en una unidad o carpeta para entrar; usa la flecha hacia arriba o cualquier elemento de la ruta de navegación para volver.

## Leer y usar la vista

- El mapa de árbol de la izquierda asigna más superficie a los elementos grandes. Los tonos claros y oscuros indican el tamaño relativo, mientras que los bordes distinguen las carpetas de los archivos.
- La lista de la derecha muestra los mismos elementos ordenados por tamaño descendente. Una barra fina bajo cada fila indica su proporción respecto al total visible de la ubicación actual. Seleccionar un elemento en una vista también lo resalta en la otra.
- Haz clic con el botón derecho en una tarjeta o fila para abrir el mismo menú de acciones de los resultados de búsqueda, con opciones para abrir, localizar, copiar y ejecutar las acciones de plugins aplicables.
- Selecciona una tarjeta o fila y usa el atajo de vista previa configurado para abrir la vista previa de la ventana de búsqueda completa; una vista previa abierta seguirá las selecciones posteriores.
- Los nombres que desbordan la lista de la derecha se desplazan mientras la fila está seleccionada o bajo el puntero, en lugar de mostrar información emergente.

## Qué se cuenta

Solo se incluyen los elementos que ya existen en los índices habilitados de Lertaro, y el analizador nunca recorre el sistema de archivos para completar lo que falta. El contenido excluido o no indexado está ausente. Los elementos ocultos se muestran con normalidad; los elementos del sistema no se muestran individualmente, aunque su tamaño todavía puede contribuir al total de una carpeta antecesora visible.

Los tamaños son tamaños lógicos de archivo, no espacio asignado en disco. Los totales de las carpetas incluyen sus descendientes indexados y los datos con vínculos físicos solo se cuentan una vez, por lo que los resultados pueden diferir del Explorador de Windows o de un analizador de disco a nivel de sectores.

Mientras la página del analizador está visible, sigue automáticamente los eventos de cambio pertinentes de los índices en memoria y agrupa las ráfagas antes de actualizar la vista actual. Nunca abre ni vuelve a cargar los archivos de caché del índice. Al iniciar una búsqueda se pausan sus actualizaciones; al cerrar la ventana de búsqueda completa se liberan los elementos renderizados y las cachés de interfaz compartidas.
