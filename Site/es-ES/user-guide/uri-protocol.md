# Protocolo URI (lertaro://)

Lertaro se registra a sí mismo como el gestor de un enlace `lertaro://` — sin ningún paso de instalación
aparte, se configura automáticamente la primera vez que se ejecuta la aplicación. Esto permite que cualquier cosa
capaz de abrir un enlace (un navegador, un acceso directo, otra aplicación, un script) salte directamente a una
parte concreta de Lertaro, en lugar de ser accesible solo mediante un atajo de teclado.

Si Lertaro aún no está en ejecución, abrir un enlace `lertaro://` lo inicia y luego sigue el enlace. Si ya está
en ejecución, la instancia en ejecución gestiona el enlace directamente — nunca inicia una segunda copia.

## Rutas

| Enlace | Qué hace |
|---|---|
| `lertaro://` | Activa la ventana de búsqueda rápida — lo mismo que invocarla con su atajo. |
| `lertaro://search/[keyword]` | Activa la ventana de búsqueda rápida con `[keyword]` ya escrito. |
| `lertaro://fullsearch/[keyword]` | Abre la ventana de búsqueda completa con `[keyword]` ya escrito. |
| `lertaro://settings/page/[section]` | Abre Configuración en una sección concreta de nivel superior. |
| `lertaro://settings/entry/[index]` | Abre Configuración y salta directamente a un ajuste concreto, resaltado. |
| `lertaro://localsend` | Abre una ventana de envío de LocalSend vacía. |
| `lertaro://localsend/items[/encoded-item...]` | Cambia al modo de archivos/carpetas y, opcionalmente, añade un segmento de ruta codificado por elemento. |
| `lertaro://localsend/text[/encoded-text]` | Cambia al modo de texto y, opcionalmente, rellena el texto codificado. |

```
lertaro://search/report
lertaro://settings/page/Appearance
```

El primero activa la ventana de búsqueda rápida ya filtrada a "report"; el segundo abre Configuración
directamente en la página Apariencia.

`[section]` coincide con una de las entradas de nivel superior de la barra lateral: `Service`, `Index`, `General`,
`Appearance`, `Hotkeys`, `Plugins`, `Favorites`, `History`, `QuickPanel`, `About` — sin distinguir mayúsculas de
minúsculas.

`[index]` no está pensado para escribirse a mano — es un número que la propia [Búsqueda de
configuración](./instant-answers) genera para el ajuste que hayas elegido, de modo que seleccionar uno de sus
resultados te devuelve directamente a esa fila exacta. No es estable entre reinicios, así que no cuentes con que
un número concreto se mantenga igual.

## Enlaces de LocalSend

Cada ruta de archivo/carpeta o valor de texto debe codificarse como un segmento de ruta URL completo. Para añadir varios elementos, agrega un segmento codificado por elemento; todas las rutas deben ser absolutas y existir previamente. Por ejemplo:

```
lertaro://localsend/items/C%3A%5CUsers%5Ctestuser%5CDesktop%5Ca.txt/D%3A%5CShared
lertaro://localsend/text/Hello%20world
```

`lertaro://localsend/items` abre la página de recopilación en modo de archivos/carpetas, mientras que `lertaro://localsend/text` la abre en modo de texto. Un enlace con contenido avanza a la selección de dispositivos, pero nunca selecciona un dispositivo ni inicia una transferencia automáticamente. Si ya hay una ventana de envío abierta, el enlace no hace nada ni cambia su contenido o estado actual. Si LocalSend está desactivado, Lertaro abre su página de configuración de LocalSend. El contenido no válido o demasiado largo hace que se ignore toda la solicitud.

## Enlaces no reconocidos

Cualquier cosa que no coincida con una ruta conocida — una errata, una sección no admitida, basura después de
`lertaro://` — se ignora en silencio. Dado que cualquier sitio web o aplicación puede invocar este protocolo sin
pedirte permiso antes, un enlace erróneo o inesperado nunca debería hacer nada sorprendente; se registra para tu
propia solución de problemas, pero no ocurre nada más.
