# Gestión de indexación

La página de Indexación controla el alcance, la frecuencia de actualización y las reglas de exclusión para discos duros locales, recursos de red, distribuciones WSL y carpetas personalizadas. Contiene cinco pestañas: **Unidades locales**, **Unidades de red**, **WSL** (visible solo si se detectan distribuciones), **Carpetas** y **Reglas de exclusión**.

## 1. Unidades locales

- **Tarjeta de estado superior**: Muestra el total de unidades físicas y elementos indexados, con un botón global de **Reconstruir índice**.
- **Filas de unidades**:
  - **Casilla de activación**: Activa o desactiva la indexación para cada partición individual.
  - **Sistema de archivos y estado**: Muestra el tipo de sistema de archivos (NTFS, ReFS, FAT32, exFAT), estado y recuento de elementos.
  - **Acciones por unidad**: Botones independientes de **Reconstruir** y **Eliminar**; muestra un botón dinámico de **Detener** durante el escaneo.
- **Seguimiento en tiempo real**: Los volúmenes NTFS / ReFS se sincronizan leyendo el diario de cambios USN de Windows; los volúmenes FAT32 / exFAT monitorizan eventos del sistema de archivos.
- **Reconstrucción sin interrupciones**: Durante la reconstrucción de una unidad, el índice existente sigue respondiendo a las consultas hasta que el nuevo termina y lo reemplaza de una sola vez. Si se interrumpe, se reanuda desde el último punto en el siguiente inicio.

## 2. Unidades de red

- **Soporte de almacenamiento en red**: Lista todas las unidades de red asignadas en Windows (SMB / NAS).
- **Modo de actualización**: Al carecer de diarios USN locales, se puede programar su sondeo:
  - **Manual** — Se actualiza solo al pulsar "Reconstruir índice".
  - **Cada 15 minutos** — Recomendado para recursos colaborativos activos.
  - **Cada hora** — Escaneo periódico equilibrado.
  - **Diario** — Ideal para almacenamiento estático o de archivo.
- **Protección contra bucles de enlaces simbólicos**: Algoritmos internos de detección de bucles evitan atascos provocados por enlaces simbólicos recursivos en carpetas NAS.

## 3. WSL (Subsistema de Windows para Linux)

Aparece automáticamente si se detecta al menos una distribución WSL instalada:

- **Detección automática**: Reconoce Ubuntu, Debian, Arch y otras distribuciones.
- **Estado y programación**: Misma gestión y modos de actualización (Manual / 15 min / Cada hora / Diario).
- **Consultas con latencia cero**: Busca directamente en el índice en memoria sin despertar ni ralentizar el subsistema Linux.

## 4. Carpetas

Útil cuando se desea indexar carpetas de trabajo específicas en lugar de volúmenes completos:

- **Adición múltiple**: Pulsa **Añadir carpeta** para abrir un selector que permite selecciones múltiples con `Ctrl` o `Shift`.
- **Rutas UNC**: Admite rutas compartidas de red (p. ej. `\\server\share\projects`).
- **Programación independiente**: Cada carpeta dispone de su propio interruptor, contador y modo de actualización.

## 5. Reglas de exclusión

Las reglas de exclusión filtran los resultados de búsqueda y limitan hasta dónde descienden los recorridos de red, WSL y carpetas. No detienen la indexación de un disco local: un volumen local siempre se recorre por completo, así que una ruta como `E:\` en esta lista solo oculta sus resultados. Para detener la indexación de un disco local, desactívalo en **Unidades locales**. Las reglas forman **una lista única**, sin subpestañas. Cada regla combina una coincidencia y un nivel; la coincidencia se deduce de cómo se escribe el texto y el cuadro de entrada lo muestra como **Detectado:**.

- **Ruta completa**: empieza con una letra de unidad (`D:\Cache`), una ruta UNC (`\\server\share`), una variable de entorno (`%ProgramData%`, `%APPDATA%`) o una ruta virtual `shell:`. Cubre esa carpeta y todo su contenido.
- **Comodín**: cualquier otra escritura. `*` coincide dentro de un mismo nivel (`*.log`), `?` con un carácter y `**` de forma recursiva (`**\node_modules\**`). Una regla sin separadores coincide por nombre a cualquier profundidad (`node_modules`, `*.tmp`); una regla con separadores se ancla desde la raíz de la unidad, de modo que `Listary\abc` alcanza `C:\Listary\abc` y su contenido, pero no `D:\work\Listary\abc`.
- **Expresión regular**: escribe entre barras, p. ej. `/^\./` para archivos ocultos y `/~\$/` para bloqueos temporales de Office. Se evalúa como coincidencia parcial sobre la ruta completa o el nombre de archivo. Usa un comodín siempre que puedas: una expresión regular se interpreta en cada coincidencia y cuesta más.

El **nivel** decide qué ocurre: **Excluir** oculta el elemento y **Baja**, **Normal** y **Alta** solo cambian el orden; una regla alta puede fijarse al principio marcando la casilla. El nivel se elige en el desplegable de la derecha de la fila o se escribe como prefijo: `!` excluir, `-` bajo, `+` alto, sin prefijo normal. Una barra invertida inicial hace literal el prefijo.

> [!NOTE]
> El prefijo solo se retira en el cuadro **Añadir...** y en **Edición por lotes**. Al editar una regla existente en su propia fila el texto se toma al pie de la letra, así que cambia el nivel con el desplegable: un `!` que queda dentro de un comodín se convierte en un carácter de la ruta y, como las rutas de Windows no llevan `!`, esa regla no puede coincidir con nada y la página no muestra ningún error.

En discos locales las reglas se aplican durante la búsqueda; en discos de red, WSL y carpetas personalizadas el índice omite lo excluido y se actualiza al aplicar. Cuando una exclusión cubre una regla de prioridad, aparece un aviso de conflictos sobre la lista y la regla cubierta no surte efecto.

> [!TIP]
> Las reglas admiten **adición individual** e **importación/exportación por lotes**: pulsa **Generar desde la Lista** para exportarlas a texto, edítalo y pulsa **Aplicar a la Lista** para actualizarlas en bloque. Si alguna línea del texto por lotes no se reconoce, no se aplica ninguna y el texto queda en el cuadro para que lo corrijas.
