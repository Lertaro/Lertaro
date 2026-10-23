# Historial

La página de Historial gestiona la frecuencia de uso por ruta, las palabras clave escritas y las asociaciones confirmadas consulta-destino en tres pestañas independientes: **Historial de búsqueda**, **Historial de palabras clave** y **Asociaciones de consulta**.

## 1. Historial de búsqueda

Registra la frecuencia y recencia con la que abres cada archivo, carpeta o aplicación para afinar el orden entre coincidencias directas equivalentes:

- **Desempate por frecuencia con decaimiento**: Los elementos habituales afinan el orden **dentro del mismo grado de coincidencia** con una semivida de 90 días, sin superar nunca a una coincidencia directa exacta de título o nombre.
- **Agrupación en la Ventana incrustada**: En los diálogos de archivos, las coincidencias dentro de la carpeta activa se clasifican en "Carpeta actual" y las demás en "Búsqueda global".
- **Filtrado de rutas inexistentes**: Si un archivo ha sido eliminado o movido externamente, se omite de forma automática, evitando enlaces rotos y resultados duplicados.
- **Opciones de gestión**:
  - **Habilitar historial**: Interruptor general; al desactivarlo se conservan los registros existentes.
  - **Filtro de búsqueda**: Permite filtrar las entradas visibles.
  - **Contadores y limpieza**: Elige un umbral y pulsa **Borrar con como máximo N usos**, elimina filas individuales o pulsa **Borrar todo el historial**.
  - **Actualización en tiempo real**: La lista actualiza automáticamente los contadores tras cada apertura.

## 2. Historial de palabras clave

Memoriza las **cadenas de búsqueda originales** que se hayan usado realmente para ejecutar un resultado o una acción, o para abrir la Ventana principal. Escribir una consulta y cerrar o abandonar la Ventana rápida no la añade:

- **Navegación por atajos**: En la Ventana rápida, pulsa **`Alt+Up`** / **`Alt+Down`** para retroceder o avanzar entre consultas recientes. Pulsa **`Ctrl+Delete`** para borrar el término activo.
- **Contadores y limpieza**: Cada palabra clave muestra cuántas veces se ha usado. Elige un umbral y pulsa **Borrar con como máximo N usos** para eliminar palabras clave poco utilizadas.
- **Gestión independiente**: Dispone de su propio interruptor de activación, filtro, eliminación individual y botón de **Borrar todo el historial**.

## 3. Asociaciones de consulta

Almacena hábitos confirmados **consulta → ruta de destino** de forma independiente a la frecuencia general:

- **Umbral de 3 aperturas orgánicas**: Un elemento solo se recupera por asociación aprendida tras abrirlo de forma orgánica al menos **3 veces** con la misma consulta normalizada, evitando que clics accidentales contaminen búsquedas futuras.
- **Sin bucles de retroalimentación**: Las aperturas desde sugerencias aprendidas no inflan contadores no confirmados y aparecen siempre detrás de las coincidencias directas.
- **Limpieza independiente**: Filtra, elimina asociaciones individuales o bórralas todas sin reiniciar la frecuencia de uso por ruta.
