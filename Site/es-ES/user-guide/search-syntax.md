# Sintaxis de búsqueda

El cuadro de consulta de Lertaro admite más que escribir texto sin más. Cada operador de abajo se puede combinar
con términos difusos normales en la misma consulta.

## Coincidencia difusa (predeterminada)

Escribe cualquier parte de un nombre y Lertaro lo encuentra siempre que los caracteres aparezcan en orden, en
cualquier parte del nombre del archivo/carpeta — no necesitas escribir una subcadena contigua:

| Escribes | Coincide con |
|---|---|
| `swlst` | `Lertaro.exe` |
| `report` | `Q3-report-final.docx` |

Desactiva esto en **Configuración → General → Sistema → Activar coincidencia difusa** y un término normal (sin
operador) tendrá que aparecer como subcadena contigua en su lugar — `abc` deja de coincidir con `a-b-c`. Todos
los operadores de la tabla de abajo siguen funcionando exactamente igual en cualquier caso; el ajuste solo
cambia lo que exige un término normal. El operador `'` invierte la exactitud de un solo término
independientemente del ajuste, así que puedes meter una palabra difusa en una consulta por lo demás exacta, o
una palabra exacta en una por lo demás difusa, sin tocar el ajuste en sí.

## Varias palabras

Separa las palabras con un espacio. Cada palabra reduce aún más el conjunto de resultados — **no** exige que las
palabras aparezcan en el mismo orden en que las escribiste:

```
report final
```

coincide igual de bien con `final-Q3-report.docx` que con `Q3-report-final.docx`.

## Sensibilidad a mayúsculas

- Una consulta **totalmente en minúsculas** no distingue mayúsculas de minúsculas: `myfile` coincide con
  `MyFile`, `MYFILE`, etc.
- Una consulta con **alguna letra mayúscula** pasa a distinguir mayúsculas de minúsculas para ese término:
  `MyFile` solo coincide con `MyFile`, no con `myfile`.

## Operadores

| Prefijo/Sufijo | Ejemplo | Efecto |
|---|---|---|
| *(ninguno)* | `report` | Coincidencia difusa en cualquier parte del nombre (predeterminado). |
| `!` | `!temp` | **Excluir** resultados cuyo nombre contenga la subcadena exacta `temp` (esta no es difusa). |
| `'` | `'report` | **Invierte la exactitud** de este término — coincidencia de subcadena exacta en lugar de difusa mientras la coincidencia difusa está activada (por defecto); vuelve a ser difusa para este término mientras la coincidencia difusa está desactivada en Configuración. |
| `'...'` | `'final report'` | Coincidencia exacta anclada a límites de palabra (no coincidirá dentro de una palabra más larga). |
| `^` | `^IMG` | Coincidencia de **prefijo** — el nombre debe empezar por `IMG`. |
| `$` | `.pdf$` | Coincidencia de **sufijo** — el nombre debe terminar en `.pdf`. |
| `^...$` | `^readme.md$` | **Igualdad** — el nombre debe ser exactamente `readme.md`. Solo cuando ambos envuelven la *misma* palabra; en palabras distintas siguen siendo filtros independientes de prefijo y sufijo. |
| `\|` | `report \| summary` | **OR** — coincide con cualquiera de los dos lados de la barra vertical. |

Puedes combinar estos libremente, por ejemplo `^IMG !.png$ 2024` encuentra archivos que empiecen por `IMG`, de
2024, que *no* sean PNG.

En una consulta OR, todo término que realmente coincida con un resultado dado se resalta en su nombre — no solo el
que coincidió primero — así que `report | summary` resalta ambas palabras en un resultado cuyo nombre las
contenga a las dos.

## Pegar varias líneas

Pega texto que contenga varias líneas — por ejemplo, nombres de archivo copiados uno por línea desde una hoja de
cálculo o un archivo de texto — y Lertaro los convierte automáticamente en una consulta OR en lugar de pegarlos
tal cual:

```
123
456
678
```

se pega como `123 | 456 | 678`, coincidiendo con cualquiera de los tres. Las líneas en blanco se omiten. Un
pegado normal de una sola línea no se ve afectado.

## Segmentar por unidad

Empieza la consulta con una letra de unidad seguida de dos puntos para restringir los resultados a esa unidad, y
luego sigue escribiendo tu búsqueda con normalidad:

```
d: report
```

busca solo en la unidad `D:`.

El espacio es opcional: `d:report` significa lo mismo que `d: report`.

## Modo de ruta

Si tu consulta contiene un separador de ruta (`\` o `/`), Lertaro cambia al modo de ruta y compara contra rutas
completas en lugar de solo nombres — útil para saltar directamente a una carpeta conocida:

```
D:\Projects\Lertaro
```

Un separador final (`D:\Projects\`) busca en el *contenido* de esa carpeta exacta.

## Filtrar por nombre de carpeta y comodines (Query Tokens)

Lertaro admite encadenar tokens de consulta después de sus palabras clave principales (con el prefijo `:` de forma predeterminada o mediante prefijos de token dedicados) para realizar un filtrado secundario:

- **Filtro comodín secundario (`:?<expresión-comodín>` o `?<expresión-comodín>`)**: Utiliza la sintaxis comodín estándar de Windows (`?` para cualquier carácter individual, `*` para cero o más caracteres) para filtrar los resultados de la búsqueda principal. Por ejemplo, `mp4 :?(2026???????????)` o `mp4 ?(2026???????????)` filtra archivos con la etiqueta 2026 y una marca de tiempo de 11 dígitos. Utilice `|` o `;` para especificar múltiples condiciones comodín OR (ej. `?(2026*)|*.png`).
- **Coincidencia de ruta (`::<expresión-ruta>`)**: Requiere que el nombre del resultado o la carpeta antecesora coincida con el texto especificado (ej. `1080 ::wallpapers` o `report ::2024`).
- **Filtros de categoría personalizados (`:@<palabra-clave>`)**: Aplica reglas de extensión de archivo o categoría preconfiguradas (ej. `:@doc` o `:@video`).

## Cuando un término describe la carpeta, no el archivo

Si la coincidencia por nombre de archivo/carpeta no llega a llenar los resultados, Lertaro los completa
automáticamente permitiendo además que los términos coincidan con carpetas antecesoras — sin necesidad de
ninguna sintaxis especial:

```
d01j dcj
```

encuentra un archivo llamado `d01j` que vive en una carpeta llamada (o con alias) `dcj`, aunque `dcj` nunca
aparezca en el propio nombre del archivo. Esto solo rellena el resto de una consulta a partir de las carpetas
por encima de un archivo — al menos un término todavía tiene que coincidir con el propio nombre del archivo, y
solo entra en acción cuando una búsqueda normal por nombre no ha llenado la página. Lo que encuentra se añade
después de esos resultados en lugar de mezclarse con ellos, así que nunca puede desplazar ni reordenar un
resultado que una búsqueda normal ya habría encontrado. Las carpetas
antecesoras se comparan de la misma forma que los nombres de archivo, así que el pinyin llega aquí también a
un nombre de carpeta en chino.

## Saltarse las reglas de exclusión para una búsqueda

Empieza una consulta con `*` para buscar más allá de tus propias [reglas de
exclusión](./settings/index-drives#reglas-de-exclusion) — `ExcludedPaths`, globs ignorados y expresiones regulares
ignoradas — solo para esa búsqueda, sin cambiar tu configuración:

```
*node_modules
```

El propio `*` se elimina antes de comparar, así que nunca se trata como parte del texto de búsqueda. Esto solo
revela resultados que ya estén indexados; una carpeta que *nunca* se indexó desde un principio (una carpeta
excluida en una unidad de red o WSL) seguirá sin aparecer. Los archivos ocultos/de sistema se siguen filtrando de
todos modos — esto solo afecta a tu propia configuración de reglas de exclusión. Escribir solo `*` sin nada detrás
todavía muestra un aviso de "sigue escribiendo para buscar" en lugar de "Sin resultados de búsqueda", ya que aún no
se ha ejecutado ninguna búsqueda de verdad.

## Activador de tipo de resultado

Opcional, y desactivado por defecto — tú mismo asignas el carácter. Si has asignado un carácter activador a un
tipo de resultado en **Configuración → General → Ventana de búsqueda rápida → Prioridad de tipo de resultado**,
escribir ese carácter como lo primero de todo en la ventana rápida muestra solo los resultados de ese tipo —
Aplicaciones, Configuración, un Filtro de archivos concreto, los propios elementos de un plugin, o simplemente
Archivos — ocultando cualquier otro tipo:

```
;vs
```

encuentra "Visual Studio" solo entre las Aplicaciones, si `;` es el activador configurado para ese tipo, sin
importar qué otro tipo de resultado hubiera coincidido mejor con el texto. Escribir solo el carácter activador sin
nada detrás todavía muestra un aviso que nombra el tipo, en lugar de "Sin resultados de búsqueda". Historial y
Favoritos no se ven afectados en ningún caso — siempre aparecen primero, haya activador o no, en las ventanas rápida y en línea. La ventana completa no añade esas entradas recuperadas a sus resultados. No hay ningún
activador configurado por defecto; ver [Configuración general](./settings/general#ventana-de-busqueda-rapida) para
configurar uno.

## Nombres de archivo en chino: alias en pinyin

Los nombres de archivo que contienen caracteres chinos se pueden buscar automáticamente por pinyin, sin necesidad
de ninguna configuración:

- **Pinyin completo**: escribir `chongqing` coincide con un archivo llamado `重庆`.
- **Iniciales**: escribir `cq` también coincide con `重庆` (primera letra de cada sílaba).
- Los **caracteres polifónicos** (caracteres con más de una pronunciación válida) generan alias para cada lectura
  habitual, así que cualquiera que sea la pronunciación en la que pienses es probable que coincida.

Esto lo gestiona un plugin de alias incluido — ver **Configuración → Plugins** si alguna vez quieres comprobar que
está habilitado.

## Nombres de archivo en español: alias de acentos

Los nombres de archivo que contienen caracteres con acentos o signos diacríticos en español (`á`, `é`, `í`, `ó`, `ú`, `ü`, `ñ`) se pueden buscar automáticamente utilizando letras ASCII simples, sin necesidad de configuración:

- **ASCII sin acentos**: al escribir `cancion` se busca `Canción.mp3`, `nino` busca `Niño.txt` y `ciguena` busca `Cigüeña.png`.
- **Resaltado completo**: los caracteres que coinciden (incluidos los caracteres con acento en el nombre original) se resaltan con precisión.

Esto es gestionado por el complemento integrado `SpanishAlias`; consulte **Configuración → Complementos** para verificar que esté activado.

## Favoritos, no alias personalizados

Lertaro no tiene un sistema genérico de "define tu propio alias/macro". Lo más parecido es
[Favoritos](./settings/favorites): fija una carpeta, archivo o URL bajo un nombre para mostrar personalizado, y se
vuelve buscable por ese nombre (mostrado con una marca ★ en los resultados). Si lo que realmente quieres es una
palabra clave personalizada que lance un programa, consulta en su lugar [Comandos
personalizados](./instant-answers#comandos-personalizados).
