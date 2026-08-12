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
| `lertaro://localsend/items?path=[encoded-path]` | Añade uno o más archivos o carpetas a LocalSend. Repite `path` para varios elementos. |
| `lertaro://localsend/text?value=[encoded-text]` | Añade texto a LocalSend. |

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

Cada ruta de archivo o valor de texto debe estar codificado para URL. Para añadir varios archivos o carpetas, repite el parámetro `path`; todas las rutas deben ser absolutas y existir previamente. Por ejemplo:

```
lertaro://localsend/items?path=C%3A%5CUsers%5Ctestuser%5CDesktop%5Ca.txt&path=D%3A%5CShared%5Cb.txt
lertaro://localsend/text?value=Hello%20world
```

Un enlace de LocalSend solo rellena la ventana de envío. Nunca selecciona un dispositivo ni inicia una transferencia automáticamente. Si LocalSend está desactivado, Lertaro abre su página de configuración de LocalSend. Los parámetros no válidos, mezclados o demasiado largos hacen que se ignore toda la solicitud.

## Enlaces no reconocidos

Cualquier cosa que no coincida con una ruta conocida — una errata, una sección no admitida, basura después de
`lertaro://` — se ignora en silencio. Dado que cualquier sitio web o aplicación puede invocar este protocolo sin
pedirte permiso antes, un enlace erróneo o inesperado nunca debería hacer nada sorprendente; se registra para tu
propia solución de problemas, pero no ocurre nada más.
