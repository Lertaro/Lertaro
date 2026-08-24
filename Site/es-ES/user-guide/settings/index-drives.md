# Gestión de indexación

La página de Indexación controla el alcance, la frecuencia de actualización y las reglas de exclusión para discos duros locales, recursos de red, distribuciones WSL y carpetas personalizadas. Contiene cinco pestañas: **Unidades locales**, **Unidades de red**, **WSL** (visible solo si se detectan distribuciones), **Carpetas** y **Reglas de exclusión**.

## 1. Unidades locales

- **Tarjeta de estado superior**: Muestra el total de unidades físicas y elementos indexados, con un botón global de **Reconstruir índice**.
- **Filas de unidades**:
  - **Casilla de activación**: Activa o desactiva la indexación para cada partición individual.
  - **Sistema de archivos y estado**: Muestra el tipo de sistema de archivos (NTFS, ReFS, FAT32, exFAT), estado y recuento de elementos.
  - **Acciones por unidad**: Botones independientes de **Reconstruir** y **Eliminar**; muestra un botón dinámico de **Detener** durante el escaneo.
- **Seguimiento en tiempo real**: Los volúmenes NTFS / ReFS se sincronizan leyendo el diario de cambios USN de Windows; los volúmenes FAT32 / exFAT monitorizan eventos del sistema de archivos.
- **Reconstrucción sin interrupciones**: Durante la reconstrucción de una unidad, el índice existente sigue respondiendo a las consultas hasta que el nuevo esté listo, realizándose un intercambio instantáneo. Si se interrumpe, se reanuda desde el último punto en el siguiente inicio.

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

Se aplican globalmente a discos locales, red y carpetas, organizadas en tres subpestañas:

### Exclusión de rutas

- **Coincidencia por prefijo**: Excluye rutas absolutas (`D:\Cache`) o variables de entorno (`%ProgramData%`, `%APPDATA%`).

### Reglas Glob

- **Sintaxis**:
  - `*`: Coincide con caracteres dentro de un mismo nivel (p. ej. `*.tmp`, `*.log`).
  - `**`: Coincidencia recursiva entre subdirectorios (p. ej. `**/node_modules/**`, `**/bin/**`, `**/obj/**`).

### Reglas de expresiones regulares

- **Filtrado avanzado**: Coincidencia mediante regex contra rutas y nombres de archivo (p. ej. `^\.` para archivos ocultos, `~\$` para temporales de Office).

> [!TIP]
> Las exclusiones admiten **adición individual** e **importación/exportación por lotes**: pulsa **Generar desde lista** para exportar las reglas a texto, edítalas y pulsa **Aplicar a la lista** para actualizarlas en bloque.
