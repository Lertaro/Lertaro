# Plugins

Lista cada plugin instalado, con la versión del SDK de Plugins cargada actualmente mostrada como una insignia en
la cabecera de la página — haz clic en ella para abrir el [Manual de Desarrollador](../../dev-guide/), que es
justamente para lo que sirve ese número de versión.

La página se divide en dos paneles: los plugins instalados a la izquierda, y los detalles del seleccionado a la
derecha. Cada uno se desplaza por su cuenta, así que una lista larga de plugins y un formulario largo de ajustes
no se arrastran mutuamente.

Si no hay ningún plugin instalado, la página muestra en su lugar un mensaje de estado vacío.

## Lista de plugins

Una fila por plugin instalado, con su nombre y debajo su versión. Al seleccionar una fila, ese plugin se abre en
el panel de la derecha.

La lista encabeza con lo que hay que hacer: primero los plugins con ajustes propios, después los que tienen
componentes que puedes desactivar, y luego el resto, por orden alfabético dentro de cada grupo.

## Plugin seleccionado

El panel empieza con el icono, el nombre y la **descripción general de la función** del plugin.

Debajo, un plugin que expone su propia configuración (ajustes personalizados más allá de un simple
habilitar/deshabilitar) obtiene dos pestañas, **Detalles** y **Configurar**. Un plugin sin nada que configurar no
muestra ninguna pestaña: sus detalles ocupan el panel entero.

### Detalles

Los componentes que registra este plugin, agrupados por tipo (proveedores de búsqueda, proveedores de menú
dinámico, etc.). Cada componente activable tiene su propia **casilla de habilitar/deshabilitar**; un componente
marcado como obligatorio muestra en su lugar un icono de candado y no se puede desactivar. Al pasar el cursor
sobre el **(!)** junto a un componente se revela su descripción detallada de función.

Cuando un grupo (o el plugin en su conjunto) tiene más de un componente activable, aparece un enlace
**Seleccionar todo / Deseleccionar todo** junto a su encabezado, que permite marcar/desmarcar todas las casillas
de ese ámbito a la vez en lugar de una por una.

### Configurar

Los ajustes propios del plugin, editados aquí mismo en lugar de en un diálogo aparte. Un plugin que reparte sus
ajustes en dos o más grupos obtiene su propia fila de pestañas, una por grupo.

No se escribe nada hasta que pulsas **Aceptar**. Al salir de esta pestaña — volviendo a Detalles, o eligiendo
otro plugin — los campos vuelven a sus valores guardados, de modo que unas ediciones que dejaste atrás no puedan
confirmarse más tarde por descuido.

Para ver un ejemplo concreto de cómo es en la práctica la configuración de un plugin (por ejemplo, cambiar una
palabra clave de activación), ver [Respuestas instantáneas y atajos de palabra clave](../instant-answers).

## Soporte para el ecosistema de plugins de Flow Launcher

Además de los plugins nativos en C# construidos con `Lertaro.PluginSdk`, Lertaro incluye compatibilidad integrada con el extenso ecosistema de plugins de Flow Launcher a través del Flow Launcher Bridge.

- **Tipos de plugins compatibles**: Plugins de Flow Launcher escritos en **C# (.NET)**, **Python 3.12** (con resolución e instalación automática de dependencias `pip` en segundo plano desde `requirements.txt`), **Node.js v20 LTS** (con resolución e instalación automática de dependencias `npm` en segundo plano desde `package.json`) y **ejecutables** independientes (`.exe`).
- **Instalación**: Coloca las carpetas de plugins de Flow Launcher de terceros en `<Directorio de datos de usuario>\FlowData\Plugins\`. Lertaro descubrirá, inicializará y registrará automáticamente sus palabras clave de activación y acciones de búsqueda.
- **Entornos de ejecución aislados**: Los entornos de Python (`FlowData\PythonEmbeded-{arch}`) y Node.js (`FlowData\NodeEmbeded-{arch}`) están totalmente aislados dentro del directorio de datos de usuario de Lertaro, descargándose e instalándose bajo demanda sin modificar ni contaminar el PATH del sistema.
- **Gestión centralizada e interruptores individuales**: En la pestaña **Configurar** de **Configuración → Complementos → Flow Launcher Bridge**, todos los plugins de Flow cargados se organizan en subpestañas dedicadas. Cada plugin cuenta con un interruptor individual para **Habilitar este complemento** según tus necesidades.
- **Palabras clave personalizadas y aislamiento de estado**: Personaliza la **palabra clave de activación (ActionKeyword)** para cada plugin de Flow. Las palabras clave personalizadas y los estados de desactivación se almacenan en `FlowData\Settings\Plugins.json`, evitando contaminar los ajustes originales del plugin y conservándose tras reinicios o actualizaciones.
- **Interfaz nativa y soporte multiidioma (i18n)**: Compatible de forma transparente con plantillas de configuración en YAML/JSON (`SettingsTemplate.yaml`/`.json`) y paneles WPF personalizados en C# (`ISettingProvider`), adaptándose automáticamente al tema de Lertaro y aplicando traducción multiidioma (i18n).
- **Búsqueda y ejecución rápida**: Escribe la palabra clave `flow` en la barra de búsqueda para listar todos los plugins de Flow Launcher cargados junto con sus palabras clave de activación y estado. Escribe la palabra clave específica de cualquier plugin (como `md` o `tr`) para realizar búsquedas directamente.

