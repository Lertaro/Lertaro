# Historial

Las tres pestañas gestionan por separado los elementos abiertos, los términos de búsqueda y las asociaciones aprendidas.

## 1. Historial de búsqueda

Registra la frecuencia y la fecha de apertura de cada archivo, carpeta o aplicación desde los resultados.

- **Ordenación**: cada apertura suma 1 punto, hasta un máximo de 10. La puntuación se reduce a la mitad cada 90 días. Con 3 puntos efectivos se opta a H3 y con 5 a H5. La búsqueda rápida asigna hasta 3 plazas H5 y 10 H3; la integrada, 1 y 3. La ventana completa no limita las plazas.
- **Alcance**: el uso permite adelantar resultados del mismo grado efectivo y prioridad. El orden de tipos de la búsqueda rápida puede reducir el grado de nombre exacto de archivos y carpetas; consulta [Sintaxis de búsqueda](../search-syntax).
- **Grupos integrados**: los resultados de la carpeta actual y los globales se muestran por separado. Se omiten las rutas inexistentes.
- **Gestión**: filtra por palabra, elimina entradas, borra las de N usos o menos, o borra todas. La lista se actualiza al abrir algo desde otra ventana. Desactivar el registro impide añadir entradas nuevas; las existentes siguen actualizando frecuencia y fecha.

## 2. Historial de palabras clave

Guarda la consulta original al ejecutar un resultado o una acción, o al abrir la ventana completa. Escribir y cerrar la búsqueda rápida no crea un registro.

- Usa **Alt+Up / Alt+Down** para recorrer consultas anteriores y **Ctrl+Delete** para eliminar la actual.
- Permite filtrar, eliminar entradas, borrar todo o limpiar por número de usos. Tiene su propio interruptor: al desactivarlo, no se añaden términos nuevos, pero los existentes siguen acumulando usos.

## 3. Asociaciones de consulta

Guarda asociaciones **consulta → ruta de destino** separadas de la puntuación general de uso.

- Una asociación se activa después de abrir un resultado normal al menos **3 veces** con la misma consulta normalizada. Abrir un resultado recuperado solo por asociación no aumenta esa cuenta.
- Se añade como máximo **una** asociación por consulta. Se excluyen los destinos de prioridad baja; los resultados recuperados solo por asociación van después de las coincidencias directas.
- Filtra, elimina o borra asociaciones sin reiniciar las puntuaciones de uso de las rutas.
