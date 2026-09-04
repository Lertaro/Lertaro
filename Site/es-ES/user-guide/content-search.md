# Búsqueda de contenido

El plugin Búsqueda de contenido busca texto dentro de documentos locales y crea el índice en segundo plano. Funciona junto con la búsqueda normal de nombres de archivo de Lertaro.

## Primeros pasos

Abre **Configuración → Plugins → Búsqueda de contenido → Configurar**, añade al menos una carpeta supervisada y guarda. La palabra clave activadora predeterminada es `cs`:

```text
cs plan del proyecto
```

La palabra clave debe ir seguida de un espacio. Si `cs` entra en conflicto con tu flujo de trabajo, puedes cambiarla en la configuración del plugin.

## Qué se indexa

- **Carpetas supervisadas**: El plugin registra las carpetas locales configuradas en el servicio de índices del anfitrión. La detección inicial e incremental usa la enumeración de directorios indexados del SDK y notificaciones agrupadas de cambios de directorio; también admite variables de entorno como `%USERPROFILE%`.
- **Extensiones**: De forma predeterminada se incluyen `txt`, `md`, `pdf`, `docx`, `docm`, `pptx`, `pptm`, `xlsx`, `xlsm` y `csv`. Puedes modificar la lista separada por comas.
- **Archivos PDF**: Se busca tanto el texto extraíble de las páginas como los valores guardados de los campos de formularios PDF rellenables.
- **Tamaño de archivo**: Los archivos que superen el límite por archivo se omiten. El índice de contenido tiene además un límite independiente; `0` significa sin límite.
- **Rutas excluidas**: Añade expresiones regulares separadas por punto y coma para excluir rutas completas. Si coincide una carpeta, también se excluye su contenido.

Solo se buscan los archivos cuyo texto se ha extraído correctamente. Los archivos binarios sin un extractor de documentos adecuado se omiten en lugar de indexarse como texto ilegible.

## Cómo buscar

Escribe `cs`, un espacio y tus palabras clave en la ventana de búsqueda rápida. Los resultados muestran un fragmento de texto y la carpeta contenedora; pulsa `Enter` para abrir el archivo seleccionado. Cuando hay resultados y no se ha elegido un filtro de tipo, esos archivos también aparecen en la ventana de búsqueda completa.

Durante la creación inicial del índice, el marcador de posición `cs` muestra cuántos archivos están indexados y cuántas tareas quedan. Cuando el observador del anfitrión informa de directorios nuevos o modificados, el plugin procesa sus archivos en segundo plano, por lo que la búsqueda normal de nombres sigue disponible. Una vez estabilizado el índice, el plugin no vuelve a recorrer periódicamente el sistema de archivos.

## Borrar y reconstruir

La configuración de Búsqueda de contenido ofrece dos acciones distintas:

- **Borrar índice** elimina el índice de contenido sin volver a analizar las carpetas supervisadas.
- **Reconstruir índice** elimina el índice de contenido y vuelve a analizar todas las carpetas supervisadas.

Estas acciones solo afectan al índice de Búsqueda de contenido; no eliminan el índice normal de nombres de archivo de Lertaro.

## Consejos

- Si un archivo no aparece, revisa su carpeta, extensión, patrones excluidos y límite de tamaño.
- Después de cambiar las carpetas o las reglas, guarda la configuración y espera a que termine el análisis en segundo plano.
- Si quieres volver a extraer archivos existentes después de cambiar las reglas, usa **Reconstruir índice**.
