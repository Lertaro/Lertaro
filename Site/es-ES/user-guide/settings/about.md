# Acerca de y actualizaciones

La página Acerca de muestra las versiones de los componentes, ofrece acceso directo a las carpetas de datos de usuario y sistema, y permite buscar actualizaciones y aplicar actualizaciones silenciosas. La página se encuentra en **Configuración → Acerca de**.

## 1. Versiones de componentes e información

La parte superior detalla las versiones independientes de los cuatro componentes principales de Lertaro:

- **Versión de App**: Interfaz de usuario WPF en primer plano.
- **Versión de Core**: Librería central de búsqueda e indexación.
- **Versión de Service**: Servicio de indexación de Windows en segundo plano (el color refleja el estado de conexión del servicio).
- **Versión de CLI**: Utilidad interactiva de línea de comandos `lff`.

Más abajo se incluyen enlaces al sitio web oficial, al repositorio de GitHub y a la documentación en línea.

## 2. Directorios de datos y rotación de 5 copias de seguridad

Enlaces interactivos para abrir los directorios de almacenamiento en el Explorador (las carpetas se crean automáticamente si no existen):

### Directorio de datos de usuario

- **Contenido**: Configuración personal (`user-settings.json`), historial de búsqueda y palabras clave, cachés y certificados de seguridad.
- **Rotación de 5 copias de seguridad**: Cada vez que se guarda la configuración, Lertaro crea automáticamente una copia de respaldo `user-settings.json.bak.1`, conservando hasta `.bak.5`. Ante cualquier fallo o corte de energía, se puede restaurar cualquiera de las últimas 5 copias.

### Directorio de datos de equipo (Machine)

- **Contenido**: Ajustes del servicio (`machine-settings.json`), caché persistente del índice de disco y registros del servicio.

### Estructura de rutas de almacenamiento

- **Versión instalable**: Los datos de usuario se guardan en `%LocalAppData%\Lertaro` y los de máquina en `%ProgramData%\Lertaro`.
- **Versión portátil**: Los datos se guardan en `Data\Users\<SID hash>` y `Data\Machine` junto al ejecutable (ver [**Aislamiento de datos en versión portátil**](../getting-started#arquitectura-de-aislamiento-de-datos-en-version-portatil)).

## 3. Comprobación y aplicación de actualizaciones

- **Buscar actualizaciones**: Consulta en línea nuevas versiones con información dinámica en el propio botón ("Buscando actualizaciones..." → "Ya tienes la última versión" o detalles de la nueva versión).
- **Vías de actualización**:
  - **Actualización silenciosa** —— Descarga e instala en segundo plano, reiniciando Lertaro de forma transparente.
  - **Ir a la página de descargas** —— Abre la página de lanzamientos de GitHub en el navegador predeterminado para su descarga manual.
- **Avisos de permisos**: Si se ejecuta con una cuenta sin permisos de administrador para reiniciar el servicio, un aviso guiará hacia la página de descarga manual.
