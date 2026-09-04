# Configuración de inicio rápido

Inicio rápido es el panel de lanzamiento que aparece debajo de la ventana de búsqueda rápida cuando el cuadro de búsqueda está vacío. Combina elementos gestionados manualmente con fuentes de datos dinámicas seleccionadas, como Favoritos e Historial.

## 1. Interruptor principal

- **Activar el panel de inicio rápido**: Activa el panel. Solo aparece en la ventana de búsqueda rápida cuando la consulta está vacía y al menos una fuente contiene datos disponibles.
- Si se desactiva, el panel no carga ninguna fuente.
- La altura se calcula a partir de la fuente con más elementos y no supera la altura máxima del área de resultados de la ventana de búsqueda rápida.

## 2. Elementos manuales

Abre **Configuración → Inicio rápido → Inicio rápido** para gestionar tus elementos:

- Añade archivos, carpetas, URL o rutas de Windows Shell. Las variables de entorno se expanden al comprobar el destino.
- El nombre visible es opcional. Si queda vacío, Lertaro lo genera a partir del destino.
- Los botones para explorar archivos y carpetas permiten seleccionar varios elementos a la vez; cada destino válido y no duplicado se añade como una entrada independiente.
- Usa el controlador de arrastre para cambiar el orden. Al pulsar editar, la misma fila se convierte en un editor en línea con controles para guardar o cancelar; el control de eliminar quita el elemento.
- En un elemento manual, pasa el ratón sobre la tarjeta y pulsa el botón de puntos suspensivos para abrir su menú; después elige **Editar** o **Eliminar**. **Editar** abre el diálogo de edición del elemento.
- Cuando hay elementos, el botón **Vaciar lista** situado junto al título elimina todos los elementos manuales de una vez; permanece oculto cuando la lista está vacía.
- Los destinos que no estén disponibles se omiten temporalmente y vuelven a aparecer cuando están disponibles.

## 3. Fuentes de datos

Abre **Configuración → Inicio rápido → Fuentes de datos** para elegir fuentes dinámicas:

- Las fuentes proceden de los proveedores de pestañas del Panel rápido instalados. Así Inicio rápido reutiliza Favoritos, Historial, Historial de Windows, Último directorio, Archivos recientes y futuras fuentes de plugins sin duplicar sus datos.
- Una fuente está activada por defecto cuando existe su proveedor. Solo se guardan en la configuración las fuentes desactivadas explícitamente.
- Solo se crea una pestaña para una fuente activada que devuelva datos en ese momento. Las fuentes vacías se ocultan.
- Usa el controlador de arrastre situado junto a una fuente para reordenar sus pestañas del panel; el orden se guarda en la configuración de Inicio rápido.
- Si no hay datos en ningún elemento manual ni fuente dinámica, el panel de Inicio rápido se oculta.

## 4. Interacción con el panel

- El número de columnas se adapta al ancho de la barra de búsqueda.
- Si solo hay una fuente, el indicador inferior de fuentes se oculta.
- Con varias fuentes, la franja inferior muestra un punto por fuente. El punto seleccionado es azul y los demás grises; al pasar el ratón por encima, el punto se expande para mostrar el nombre de la fuente.
- Mantén pulsada la tecla **Shift** y usa la rueda del ratón sobre el panel para recorrer las fuentes. La fuente seleccionada reproduce brevemente la misma animación de expansión.
- Los atajos configurados de **Seleccionar elemento siguiente** y **Seleccionar elemento anterior** (por defecto **Ctrl+N** y **Ctrl+P**) también recorren las fuentes cuando el panel está visible; la selección vuelve de la última fuente a la primera y viceversa.
- Con el panel visible, usa las flechas para mover la selección según la cuadrícula visual: **←** y **→** atraviesan los límites de las filas, mientras **↑** y **↓** buscan el elemento más cercano de la misma columna en la dirección correspondiente, incluso entre grupos. Si no existe ningún elemento de esa columna en toda la dirección adyacente, la selección permanece donde está y no vuelve al principio ni al final.
- Haz clic derecho en cualquier elemento de cualquier fuente para abrir su menú de acciones flotante dentro del panel de Inicio rápido. El panel se amplía temporalmente hasta la altura máxima de trabajo de la lista de acciones y recupera su altura anterior al cerrar el menú; al hacer clic fuera del menú, este se cierra.
- Si los elementos superan el alto visible del panel, usa la rueda del ratón sobre el área de elementos para desplazarte verticalmente; pasar el ratón sobre un elemento no bloquea el desplazamiento.
- Al empezar a escribir una consulta, el panel se oculta.
