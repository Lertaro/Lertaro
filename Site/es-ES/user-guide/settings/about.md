# Acerca de

Muestra los números de versión de los componentes App, Core, Service y CLI (con color según si el servicio está
sano en ese momento), una breve descripción de Lertaro, y enlaces a la página de inicio del proyecto y a la guía
de usuario en línea.

## Carpetas de datos

Dos enlaces más, justo debajo de esos, abren las carpetas desde las que Lertaro lee y en las que escribe su
propia configuración — cada uno muestra la ruta real como texto del enlace pulsable, creando primero la carpeta si
aún no existe:

- **Carpeta de datos de usuario** — la carpeta por usuario que contiene `user-settings.json`. Cada vez que se guarda la configuración, el archivo anterior se rota a `user-settings.json.bak.1` (desplazando hacia abajo cualquier copia de seguridad más antigua, hasta `.bak.5`) antes de escribir el nuevo, de modo que una edición defectuosa o un fallo a mitad de guardado siempre deja una copia reciente desde la que restaurar. También contiene el historial, certificados y cachés específicos del usuario.
- **Carpeta de datos del equipo** — la carpeta de todo el equipo usada por el servicio en segundo plano (`machine-settings.json`, cachés del índice de unidades locales y registros del servicio).

La versión instalada usa `%LocalAppData%\Lertaro` y `%ProgramData%\Lertaro`. La versión portátil usa `Data\Users\<SID hash>` y `Data\Machine` junto a la aplicación; si la carpeta portátil `Data` aún no se ha creado, reutiliza los datos existentes de la instalación por compatibilidad. Consulta [Datos portátiles](../getting-started#datos-portátiles) para conocer la prioridad.

## Buscar actualizaciones

- Botón **Buscar actualización** — consulta si hay una versión más reciente; la propia etiqueta del botón refleja
  el progreso ("Comprobando...", y luego "actualizado" o el número de la nueva versión encontrada).
- Si una cuenta sin privilegios de administrador no puede detener el servicio en segundo plano para instalar una
  actualización en el sitio, un banner de aviso lo explica y te remite en su lugar a la página de descarga manual.
- Una vez encontrada una nueva versión:
  - **Actualización automática silenciosa** — descarga e instala en segundo plano, mostrando una barra de
    progreso, y luego reinicia Lertaro automáticamente.
  - **Ir a la página de descarga** — abre la página de la versión en GitHub en tu navegador para una instalación
    manual.

Esto es un reflejo de las casillas **Buscar actualizaciones automáticamente** / **Actualización silenciosa
automática** en [General → Sistema](./general#sistema) — esas controlan si esta comprobación ocurre
automáticamente al iniciar; esta página te permite dispararla manualmente en cualquier momento.
