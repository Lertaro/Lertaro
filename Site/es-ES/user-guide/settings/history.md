# Historial

La página de Historial gestiona los registros de uso y las prioridades de reapertura adaptativa. Contiene dos pestañas: **Historial de búsqueda** e **Historial de palabras clave**.

## 1. Historial de búsqueda

Registra los resultados ejecutados asociando el término de búsqueda con la ruta física del elemento:

- **Reapertura adaptativa priorizada**: Al escribir caracteres similares a consultas anteriores, Lertaro prioriza los elementos abiertos previamente en la parte superior. Por ejemplo, si abriste `BCompare.exe` buscando `bcomp`, una búsqueda futura de `bc` lo destacará al inicio.
- **Agrupación en la Ventana incrustada**: En los diálogos de archivos, las coincidencias del historial dentro de la carpeta activa se clasifican en "Carpeta actual" y las demás en "Búsqueda global".
- **Filtrado de rutas inexistentes**: Si un archivo ha sido eliminado o movido externamente, se omite de forma automática, evitando enlaces rotos y resultados duplicados.
- **Opciones de gestión**:
  - **Habilitar historial**: Interruptor general; al desactivarlo se conservan los registros existentes. No se añaden entradas nuevas, pero volver a abrir una entrada existente sigue incrementando su contador de usos.
  - **Filtro de búsqueda**: Permite filtrar las entradas visibles.
  - **Contadores y limpieza**: Cada entrada muestra cuántas veces se ha abierto. Elige un umbral y pulsa **Borrar con como máximo N usos** para eliminar entradas poco utilizadas; también puedes eliminar filas individuales o pulsar **Borrar todo el historial**.
  - **Actualización en tiempo real**: La lista actualiza automáticamente los contadores cuando otra ventana de búsqueda registra una nueva apertura.

## 2. Historial de palabras clave

Memoriza las **cadenas de búsqueda originales** que se hayan usado realmente para ejecutar un resultado o una acción, o para abrir la Ventana principal. Escribir una consulta y cerrar o abandonar la Ventana rápida no la añade:

- **Navegación por atajos**: En la Ventana rápida, pulsa **`Alt+Up`** / **`Alt+Down`** para retroceder o avanzar entre consultas recientes. Pulsa **`Ctrl+Delete`** para borrar el término activo.
- **Contadores y limpieza**: Cada palabra clave muestra cuántas veces se ha usado. Elige un umbral y pulsa **Borrar con como máximo N usos** para eliminar palabras clave poco utilizadas.
- **Gestión independiente**: Dispone de su propio interruptor de activación, filtro, eliminación individual y botón de **Borrar todo el historial**. Aunque esté desactivado, las palabras clave existentes siguen acumulando usos y las nuevas no se añaden.
