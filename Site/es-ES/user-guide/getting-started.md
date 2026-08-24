# Primeros pasos

## Instalación

Consigue la última versión desde el [botón de descarga](../) de la página de inicio — se publican dos variantes en
cada versión:

- **Instalador** (`Lertaro-Setup.exe`) — recomendado. Registra el servicio de indexación en segundo plano y
  puede iniciar Lertaro junto con Windows.
- **Portable** (`Lertaro-Portable.zip`) — descomprímelo y ejecútalo, sin instalación. Aun así, puedes instalar
  más adelante el servicio en segundo plano desde **Configuración → Estado del Servicio**. Si tu equipo todavía no
  tiene el .NET Desktop Runtime que necesita Lertaro, ejecuta una vez el `install-dotnet-runtime.bat` incluido —
  el instalador gestiona este paso automáticamente, pero la versión portable no puede. Cuando termines con una
  instalación portable, no hay ningún desinstalador que limpie después de ti: ejecuta el `portable-cleanup.bat`
  incluido antes de borrar la carpeta. Si el servicio está instalado, detiene y elimina el servicio de segundo plano
  de Lertaro, y también elimina el registro del protocolo URI `lertaro://` y la entrada de "iniciar con Windows"
  del usuario actual. La eliminación del servicio puede solicitar permisos de administrador; el resto de la limpieza
  se realiza para el usuario actual.

Cada una de ellas se publica para **x64** y para **ARM64**. Los nombres de arriba son las versiones x64, que
funcionan en cualquier PC reciente — incluido Windows on ARM, donde se ejecutan emuladas. En un equipo ARM es
preferible `Lertaro-Setup-arm64.exe` o `Lertaro-Portable-arm64.zip`, que son nativas. Las actualizaciones
automáticas mantienen la arquitectura que instalaste, así que pasar de una a otra implica descargar la otra
versión tú mismo.

## Datos portátiles

Una copia portátil guarda los datos del equipo en `Data\Machine` y los datos privados de cada usuario en `Data\Users\<SID hash>`, junto a la aplicación. Si todavía no existe su carpeta `Data`, reutiliza los datos existentes de `%ProgramData%\Lertaro` y `%LocalAppData%\Lertaro` por compatibilidad; crea `Data` cuando quieras que la copia portátil tenga prioridad y sea autosuficiente.

En el primer arranque, Lertaro instala e inicia un servicio de Windows (`Lertaro.Service`) que se encarga de
la indexación de archivos. Esta división existe a propósito — ver [Arquitectura](../dev-guide/architecture) si
tienes curiosidad por saber por qué — pero, como usuario, lo único que necesitas saber es: **Configuración →
Estado del Servicio** te dice si el servicio está instalado y en ejecución, y te permite instalarlo si no lo está.

## Las tres ventanas

Lertaro no tiene una sola ventana de búsqueda — se adapta a cómo la estés usando:

- **Ventana principal** — la ventana completa que obtienes desde la barra de tareas o el acceso directo del menú
  Inicio, con la lista de resultados más grande y un panel de Acciones dentro de la propia ventana.
- **Ventana rápida** — la ventana emergente compacta, siempre visible, que invocas con el atajo global de
  mostrar/ocultar (doble pulsación de `Ctrl` por defecto). Pensada para la memoria muscular de "pulsar atajo →
  escribir → Intro".
- **Ventana en línea** — incrusta una barra de búsqueda de Lertaro directamente en un diálogo de archivos nativo
  compatible o en una ventana del Explorador de archivos, de modo que puedas buscar sin salir del diálogo en el
  que ya estás.

Las tres comparten el mismo motor de búsqueda, el mismo sistema de atajos y el mismo menú de Acciones — la
diferencia está puramente en dónde y cómo aparecen.

## Búsqueda básica

Los archivos recientes, los favoritos y el historial están al alcance sin escribir nada — son pestañas del
[Panel Rápido](./settings/quick-panel), que se abre sobre la ventana que tengas delante en lugar de dentro de la
ventana de búsqueda.

Simplemente empieza a escribir. Los resultados se actualizan a medida que escribes, ordenados por relevancia (ver
[Sintaxis de búsqueda](./search-syntax) para saber cómo funcionan la coincidencia y la clasificación). Usa los
[atajos configurables de elemento siguiente/anterior](./hotkeys) (teclas de flecha por defecto) para mover la
selección, e Intro para abrir el resultado resaltado.

A continuación: [Sintaxis de búsqueda](./search-syntax) para sacarle el máximo partido al cuadro de consulta.
