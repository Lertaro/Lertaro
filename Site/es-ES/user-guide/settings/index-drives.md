# Índice

Cinco pestañas, en orden: **Unidades locales**, **Unidades de red**, **WSL** (solo se muestra al detectar una distribución), **Carpetas**, y **Reglas de exclusión**.

## Unidades locales

- Tarjeta de estado que resume cuántas unidades y elementos están indexados, más un botón **Reconstruir índice** para un reescaneo completo.
- Una fila por unidad local: una **casilla de habilitar/deshabilitar**, nombre de la unidad, sistema de archivos (NTFS/ReFS/...), estado actual, número de elementos indexados, y una acción por fila de **Reconstruir**/**Eliminar** — más una acción **Detener** mientras hay una reconstrucción en curso, para toda unidad excepto las NTFS reales (su escaneo no tiene un punto seguro donde interrumpirse).
- Las unidades NTFS y ReFS registran los cambios de forma continua a través del USN Journal de Windows; otros sistemas de archivos locales (FAT32, exFAT, ...) no tienen ningún journal que leer, así que se vigilan directamente en busca de cambios en su lugar. En cualquier caso, rara vez hace falta una reconstrucción manual, pero está ahí por si algo parece desincronizado — y si una se interrumpe (se detiene, o la app/servicio se reinicia a mitad de escaneo), la siguiente reconstrucción continúa desde donde se quedó en lugar de empezar de cero.

La búsqueda sigue funcionando mientras se reconstruye un índice. La unidad que se está reconstruyendo continúa respondiendo con su índice anterior hasta que el nuevo esté listo, y las demás unidades no se ven afectadas: una reconstrucción nunca te deja con una lista de resultados vacía.

## Unidades de red

- La misma tarjeta de estado y botón **Reconstruir índice** que Unidades locales.
- Una fila por cada unidad de red asignada: casilla de habilitar, ruta/nombre, estado (Indexando / Lista / En caché / Fallida / Pendiente / Conectada), número de elementos, y un desplegable **Modo de actualización**:
  - **Manual** — solo se actualiza bajo demanda (mediante Reconstruir índice).
  - **Cada 15 minutos**
  - **Cada hora**
  - **Diario**

Los recursos de red no exponen un journal de cambios como sí lo hacen los volúmenes NTFS locales, por eso se actualizan según una programación en lugar de en tiempo real. El motor de escaneo incluye un mecanismo integrado de seguimiento de rutas y detección de bucles de enlaces simbólicos, interceptando bucles infinitos en recursos compartidos NAS/SMB para garantizar un recuento preciso.

## WSL

Solo se muestra al detectarse al menos una distribución de WSL — la misma estructura que Unidades de red (tarjeta de estado, botón **Reconstruir índice**, y una fila por distribución con estado/número de elementos/**Modo de actualización**). Las distribuciones se detectan automáticamente; no hay ningún paso manual de "añadir".

## Carpetas

Indexa carpetas individuales arbitrarias en lugar de una unidad o recurso compartido entero — útil para indexar solo un subárbol sin arrastrar todo lo demás de ese volumen.

- Un botón **Añadir carpeta** abre un selector de carpetas; un botón **Reconstruir índice** vuelve a escanear cada carpeta de la lista.
- Una fila por carpeta añadida: casilla de habilitar, ruta, estado, número de elementos, y el mismo desplegable **Modo de actualización** (Manual / Cada 15 minutos / Cada hora / Diario) que las unidades de red — las carpetas se escanean según una programación en lugar de vigilarse de forma continua, igual que los recursos de red.
- El selector de carpetas también acepta una **ruta de recurso compartido de red UNC** (por ejemplo, `\\servidor\recurso`, o una subcarpeta dentro de ella) navegando a través de *Red* en el selector — útil para indexar un único recurso compartido o subárbol sin añadir el servidor entero como una unidad de red asignada.

## Reglas de exclusión

Tres subpestañas, cada una con la misma estructura: un cuadro de texto de una sola entrada + botón **Añadir**, una lista de reglas existentes (cada una editable/eliminable), y un cuadro de texto masivo de varias líneas con botones **Generar desde la lista** / **Aplicar a la lista** para editar todo a la vez.

- **Exclusiones de ruta** — rutas completas o variables de entorno (por ejemplo, `D:\Cache`, `%ProgramData%`).
- **Comodines glob** — `*` (cualquier carácter en un nombre de archivo), `?` (un solo carácter), `**` (directorios recursivos). Ejemplos: `*.tmp`, `**/node_modules/**`, `bin/**`.
- **Patrones regex** — expresiones regulares arbitrarias comparadas contra la ruta/nombre de archivo (coincidencia parcial). Ejemplos: `^\.` (archivos ocultos), `~$` (archivos temporales de Office), `\.git\`.

Las exclusiones se aplican por igual a la indexación local, de red y de carpetas, y las unidades/carpetas de red se vuelven a escanear automáticamente después de aplicar cambios en las reglas de exclusión.
