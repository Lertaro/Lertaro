# Gestores de archivos compatibles

Lertaro no solo busca — también puede integrarse con el gestor de archivos o el diálogo que estés usando en ese
momento. Según el destino, esa integración puede significar hasta tres cosas:

- **Acoplamiento de búsqueda en línea** — una barra de búsqueda de Lertaro se incrusta directamente en la
  ventana de destino (la [ventana en línea](./getting-started#las-tres-ventanas)), de modo que puedas buscar sin
  salir de ella.
- **Navegación rápida** — el destino responde a los [disparadores de ratón de navegación
  rápida](./hotkeys#navegacion-rapida-raton) (doble clic/clic central, o el propio logotipo de la ventana en
  línea), mostrando un menú en cascada de Favoritos, Historial y carpetas de acceso rápido.
- **Detección de ruta activa** — Lertaro puede saber qué carpeta está abierta en ese momento en el destino, de
  modo que pueda restringir una búsqueda a esa carpeta y resolver acciones relativas a la ruta (como Copiar ruta)
  contra ella.

No todas las integraciones ofrecen las tres — ver la tabla de abajo.

## Carpetas abiertas en Navegación rápida

Con **Mostrar carpetas abiertas** habilitado en el plugin Folder Cascader (activado de forma predeterminada), Navegación rápida añade un grupo de **Carpetas abiertas** antes de Favoritos e Historial. Reúne las carpetas reales del sistema de archivos que están abiertas en todos los gestores compatibles y elimina automáticamente las rutas duplicadas. Las ubicaciones virtuales y las carpetas que ya no existen se omiten, de modo que cada elemento de la lista sigue siendo un destino de navegación válido.

## Integrado (sin instalación adicional)

Estos vienen incluidos con el plugin de extensiones básicas de Lertaro — nada que habilitar por separado.

| Destino | Acoplamiento de búsqueda en línea | Navegación rápida | Detección de ruta activa |
|---|---|---|---|
| Explorador de archivos de Windows | Sí | Doble clic o clic central | Sí |
| Diálogo moderno Abrir/Guardar | Sí (esto *es* la ventana en línea) | Clic central, o clic izquierdo en el propio logotipo de la ventana en línea | — |
| Diálogo clásico Abrir/Guardar | Sí | Clic central, o clic izquierdo en el propio logotipo de la ventana en línea | — |
| Diálogo clásico Examinar carpetas | Sí | Clic central, o clic izquierdo en el propio logotipo de la ventana en línea | — |

La detección de ruta activa no se aplica a los propios diálogos — no hay "otra ventana" contra la que Lertaro
pueda restringir una búsqueda una vez que ya está acoplado dentro del diálogo.

## Plugins opcionales (Configuración → Plugins)

Cada uno de estos se distribuye como su propio plugin. Instálalo/habilítalo desde [Configuración →
Plugins](./settings/plugins), y luego usa el propio diálogo **Configurar** de ese plugin para activar **Habilitar
búsqueda en línea** y **Habilitar navegación rápida** de forma independiente — ambos vienen activados por defecto.

| Destino | Acoplamiento de búsqueda en línea | Navegación rápida | Detección de ruta activa |
|---|---|---|---|
| Directory Opus | Sí | Clic central en un panel de lista de archivos | Sí |
| Files | Sí | Clic central en un panel de lista de archivos | Sí |
| One Commander | Sí | Clic central en un panel de lista de archivos | Sí |
| Total Commander | Sí | Clic central en un panel de lista de archivos | Sí |
| XYplorer | Sí | Clic central en un panel de lista de archivos | Sí |

Los cinco detectan su destino haciendo coincidir el proceso en ejecución (y, en el caso de Directory Opus y Total
Commander, comunicándose con su interfaz de control remoto documentada a través de `WM_COPYDATA`, en lugar de leer
la interfaz); Files y One Commander usan en su lugar UI Automation, ya que ninguno expone un protocolo de control
remoto.

## Tamaños de carpetas indexadas en Directory Opus

Cuando está habilitada la opción **Activar columna Tamaño de Lertaro** del plugin de Directory Opus (lo está de forma predeterminada), Lertaro instala en Directory Opus una columna de script llamada **Lertaro Size**. Muestra el tamaño recursivo de cada carpeta indexada desde el índice actual que Lertaro mantiene en memoria; no analiza los discos. Las filas de archivos continúan usando la columna de tamaño nativa de Directory Opus. Una carpeta que Lertaro no indexa no muestra ningún valor en esta columna. Al desactivar la opción, Lertaro deja de escribir o actualizar el script; el script ya instalado se conserva intencionalmente.

Agrega **Lertaro Size** desde el selector de columnas de Directory Opus. Para conservar la columna al cambiar de carpeta y después de reiniciar Directory Opus, usa **Folder → Folder Format → Save → Save format for all folders**. Un Folder Format más específico, como uno para unidades locales o de red, tiene prioridad sobre el valor predeterminado del usuario; agrega también la columna a ese formato cuando sea necesario.

El script de tamaños generado es compatible con Directory Opus 11, 12 y 13 o posteriores; las versiones antiguas usan un ejecutor de compatibilidad oculto y eliminan la salida temporal inmediatamente después de cada consulta.

## Diálogos propios de aplicaciones (plugins opcionales)

Estos apuntan a un diálogo concreto dentro de una aplicación de terceros — no a la aplicación
entera — de la misma forma que hacen los diálogos integrados de arriba. Instálalo/habilítalo desde
[Configuración → Plugins](./settings/plugins); cada uno distribuye un único componente con su propio interruptor
de encendido/apagado ahí (sin diálogo Configurar independiente, ya que solo hay una cosa que activar o desactivar).

| Destino | Acoplamiento de búsqueda en línea | Navegación rápida | Detección de ruta activa |
|---|---|---|---|
| Diálogo Abrir/Guardar de WPS Office | Sí | Clic central, o clic izquierdo en el propio logotipo de la ventana en línea | — |
| Diálogo Extraer de WinRAR | Sí | Clic central, o clic izquierdo en el propio logotipo de la ventana en línea | — |
| Diálogo Extraer de Bandizip | Sí | Clic central, o clic izquierdo en el propio logotipo de la ventana en línea | — |
| Diálogo Añadir archivos de Bandizip | Sí | Clic central, o clic izquierdo en el propio logotipo de la ventana en línea | — |

Se detectan por estructura de controles, no por título de ventana, así que esto funciona en todos los paquetes de
idioma que distribuye cada aplicación. La entrada de WPS cubre Writer, Hojas de cálculo, Presentación y el lector
de PDF, que comparten el mismo diálogo: WPS usa el suyo propio en lugar del de Windows, y por eso necesita un
plugin, mientras que la mayoría de aplicaciones quedan cubiertas por los diálogos integrados de arriba. Igual que
con ellos, la detección de ruta activa no se aplica — Lertaro ya está acoplado dentro del propio diálogo, sin
ninguna otra ventana contra la que restringir una búsqueda.

---

¿Vas a crear tu propia integración para un gestor de archivos que no aparece aquí? Consulta la referencia de
[Adaptadores de sistema y de diálogo](../dev-guide/sdk/system-adapters) en el Manual de Desarrollador.
