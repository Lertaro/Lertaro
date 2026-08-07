# Respuestas instantáneas y atajos de palabra clave

Algunos resultados aparecen al instante mientras escribes, sin esperar a una búsqueda de archivos — o bien siempre
activos, o bien reservados detrás de una palabra clave corta que escribes antes de tu consulta real (para que una
función grande como la búsqueda en el historial del navegador no compita por la atención en cada pulsación de
tecla no relacionada). La mayoría de los que se activan por palabra clave tienen su palabra clave configurada en
**Configuración → Plugins → Configurar**.

## Siempre activos

Estos no necesitan ninguna palabra clave — se activan en cuanto lo que escribes coincide con su patrón.

### Calculadora

Escribe una expresión matemática directamente y el resultado aparece en vivo, con **Intro** copiándolo al
portapapeles:

```
12 * (4 + 3)
```

También se admite la conversión explícita de base:

```
255 to hex
0xFF to dec
```

### Variables de entorno

- `%NOMBRE%` expande una variable concreta (incluidas variables multirruta como `%PATH%`, dividida en una entrada
  por ruta).
- `%parcial` lista de forma difusa toda variable cuyo nombre coincida con `parcial`.

### Ejecutar un comando

- `#<comando>` abre una ventana del símbolo del sistema y ejecuta `<comando>` **como Administrador**.
- `$<comando>` abre una ventana del símbolo del sistema y ejecuta `<comando>` con normalidad.

### URL sueltas

Escribe o pega una dirección `http://`/`https://` y Lertaro te ofrece abrirla directamente.

## Activados por palabra clave (integrados, prefijo configurable)

Escribe la palabra clave, un espacio y luego tu consulta. Cada palabra clave usa por defecto un prefijo corto,
pero se puede cambiar de forma independiente en el propio diálogo **Configurar** de ese plugin si coincide con
algo que escribas a menudo.

| Palabra clave (por defecto) | Plugin | Qué busca |
|---|---|---|
| `ps` | Gestor de procesos | Procesos en ejecución por nombre, PID o título de ventana (también coincidencias difusas/pinyin) — selecciona uno para eliminarlo. |
| `win` | Cambio de ventana | Las ventanas actualmente abiertas y activables — el mismo conjunto que muestra Alt+Tab — por título, nombre de proceso o PID. Selecciona una para traerla al primer plano. |
| `bm` | Datos del navegador | Marcadores e historial de los perfiles de navegador de la familia Chrome/Chromium y de la familia Firefox que hayas añadido en la propia configuración de ese plugin. |
| `set` | Core Extensions | Cada ajuste de la aplicación, con coincidencia difusa por nombre — elige uno para saltar directamente a él en Configuración, resaltado. Escribe `set` sin nada detrás para listar todos los ajustes. |

Cambio de ventana muestra por defecto una miniatura en vivo del contenido real de cada ventana como su icono,
capturada en segundo plano para que nunca ralentice la escritura — un icono de aplicación normal aparece de
inmediato, y la miniatura aparece gradualmente en cuanto está lista. Desactiva **Mostrar contenido de la ventana
como icono** en **Configuración → Plugins → Cambio de ventana → Configurar** para omitir por completo esa captura
y usar siempre el icono de la aplicación. Una ventana que se ejecute en pantalla completa exclusiva real (la
mayoría de los juegos usan por defecto pantalla completa sin bordes, que esto gestiona bien) no se puede capturar
de esta forma y siempre recurre a su icono de aplicación.

Datos del navegador indexa marcadores e historial de forma independiente — **Indexar marcadores** e **Indexar
historial** son interruptores separados en **Configuración → Plugins → Datos del navegador → Configurar**, así que
puedes desactivar el historial (suele crecer mucho más que los marcadores) y seguir buscando en los marcadores, o
al revés.

## Búsqueda web

Búsqueda web incluye palabras clave predeterminadas para varios motores/sitios — `bd` (Baidu), `g` (Google),
`bing` (Bing), `gh` (GitHub), `wiki` (Wikipedia), `yt` (YouTube) — y te permite añadir, editar o eliminar entradas
por completo, cada una con su propio nombre, palabra clave, icono y plantilla de URL, desde **Configuración →
Plugins → Búsqueda web → Configurar**.

```
g lertaro github
```

abre una búsqueda de Google de "lertaro github" en tu navegador.

## Traducción

La palabra clave predeterminada del complemento de traducción es `tr`. Escribe texto después de ella para traducirlo al idioma seleccionado actualmente para la interfaz de Lertaro:

```
tr hello
```

El resultado aparece de forma asíncrona; pulsa **Entrar** o haz clic para copiarlo. El texto que se va a traducir se envía a Microsoft Translator, por lo que esta función requiere conexión a Internet. El idioma de destino es el idioma actual de la App. Puedes cambiar la palabra clave en **Configuración → Plugins → Traducción → Configurar**.

## Filtros de archivos

Indexa carpetas concretas bajo su propia regla, configurado por completo en **Configuración → Plugins → Filtros de
archivos → Configurar**. Cada regla tiene su propia lista de **Carpetas de destino** (escaneadas recursivamente),
un campo **Extensiones / Patrón** (por ejemplo, `*.exe;*.lnk` — se admiten varios patrones separados por `;` o
`,`; el `*` por defecto coincide con cualquier archivo), y un **Nombre de filtro** opcional mostrado en la
descripción del resultado. Las subcarpetas siempre se incluyen sin importar el patrón — solo los archivos se
filtran por él.

Añade una **Palabra clave de acceso directo** para restringir las coincidencias de una regla detrás de un
prefijo, igual que las palabras clave integradas de arriba, en lugar de mezclarlas siempre en el índice general.

## Totalmente personalizados (tú defines la palabra clave)

### Comandos personalizados

Define tus propios comandos `<palabra clave> <argumentos>` que lancen un programa externo, configurados por
completo en **Configuración → Plugins → Comandos personalizados → Configurar**. La plantilla de parámetros del
comando admite marcadores posicionales (`%s1`, `%s2`, ... para cada argumento separado por espacios) y un
marcador de "todo el resto" (`%s` para todo lo escrito después de la palabra clave).

Cada comando también tiene una casilla **Mostrar en Navegación rápida** (desactivada por defecto) — actívala para
listar también ese comando como su propia entrada en la raíz del menú de [Navegación
rápida](./hotkeys#navegacion-rapida-raton), pulsable sin necesidad de escribir su palabra clave en absoluto. El
campo **Submenú de Navegación rápida** lo anida bajo un submenú con nombre en lugar de la raíz — usa `/` para
anidar varios niveles de profundidad (por ejemplo, `Herramientas/Red` para un submenú de dos niveles); déjalo
vacío para mantener el comando en el nivel superior. Un comando mostrado de esta forma se ejecuta con sus
parámetros configurados tal cual, ya que aquí no hay texto de argumento escrito que sustituir en `%s1`/`%s`.

---

Ninguno de los plugins de esta página es obligatorio — cada uno se puede deshabilitar de forma independiente en
[Configuración → Plugins](./settings/plugins) si no quieres que compita por espacio de teclado, y [Sintaxis de
búsqueda](./search-syntax) cubre el lenguaje de consulta difuso, separado y siempre activo, usado para todo lo
demás (archivos, carpetas, aplicaciones).
