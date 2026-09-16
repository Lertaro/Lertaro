# Sintaxis de búsqueda

La barra de búsqueda de Lertaro admite mucho más que una simple búsqueda de texto plano. Equipada con un algoritmo de coincidencia ultrarrápido, admite coincidencia difusa con salto de caracteres, lógica booleana, exclusión, delimitación por unidad y ruta, tokens de consulta de ordenación y filtrado, y alias multilingües. Todas las sintaxis se pueden combinar libremente en la misma consulta.

## 1. Coincidencia básica y distinción entre mayúsculas y minúsculas

### Coincidencia difusa (predeterminada)

Lertaro activa la coincidencia difusa de forma predeterminada. Basta con escribir cualquier carácter en orden y coincidirá aunque los caracteres estén dispersos por el nombre del archivo o de la carpeta:

| Ejemplo de entrada | Resultado de coincidencia | Descripción |
| :--- | :--- | :--- |
| `ltro` | `Lertaro.exe` | Los caracteres coinciden en orden: `l` → `t` → `r` → `o` (**L**er**t**a**ro**.exe) |
| `vsc` | `Visual Studio Code.lnk` | Coincide con las iniciales de cada palabra (**V**isual **S**tudio **C**ode) |
| `rt-fin` | `Q3-report-final.docx` | Coincide con la subcadena contigua (Q3-repo**rt-fin**al.docx) |

Desactívala en **Configuración → General → Sistema → Habilitar coincidencia difusa** y cada término simple exigirá una subcadena contigua: `abc` solo coincidirá con nombres que contengan `abc` contiguo, y ya no coincidirá con `a-b-c`. Este interruptor solo afecta a los términos y exclusiones normales; la sintaxis de tokens descrita a continuación no se ve afectada en ningún caso.

### Sin distinción entre mayúsculas y minúsculas

La coincidencia ignora siempre las mayúsculas y minúsculas, en ambos sentidos: las mayúsculas que escribas nunca cambian lo que coincide, y las del nombre del archivo tampoco. `myfile`, `MyFile` y `MYFILE` coinciden entre sí.

No existe ningún modo sensible a mayúsculas: escribir una mayúscula nunca restringe un término a coincidencias con mayúsculas exactas.

### Alias de pinyin y orden de prioridad

Los nombres en chino se pueden buscar por pinyin, en dos formas: las **iniciales** (una letra por carácter; `ex` para 恶性) y la **lectura completa** (cada sílaba deletreada; `zhengshu` para 证书).

**Orden: posición de la coincidencia > cobertura > inglés > iniciales > pinyin completo.** Primero gana la coincidencia que empieza más a la izquierda; después, la que es más compacta y completa; el inglés, las iniciales y el pinyin completo solo separan resultados que ya coinciden en ambos criterios. Así que una coincidencia en inglés ya no supera automáticamente a una por pinyin: lo hace cuando las dos están igual de bien situadas y de ajustadas. Una última sílaba parcial sigue coincidiendo, de modo que `zhengsh` sigue encontrando 证书 mientras terminas de escribir.

**Con la coincidencia difusa desactivada**, las coincidencias por pinyin deben alinearse con el inicio de una palabra. `ex` encuentra 恶性 (las iniciales de dos caracteres) pero no 学习 (que tendría que empalmar el final de `xue` con el principio de `xi`). Con la coincidencia difusa activada, esa lectura laxa es justo lo que pediste y sigue disponible.

## 2. Varios términos y lógica booleana

### Espacio: AND

Separa varios términos de búsqueda con espacios para exigir que se cumplan todas las condiciones. El orden en el que aparecen los términos en el nombre del archivo **no importa**:

```text
report final 2024
```

La consulta anterior coincide tanto con `2024-Q3-report-final.docx` como con `final_report_2024.pdf`.

### Barra vertical `|`: OR

Usa el símbolo de barra vertical `|` para separar términos cuando baste con que coincida una sola de las alternativas:

```text
png | jpg | gif
```

Puedes combinar libremente la lógica AND y OR:

```text
report | summary
```

Esto encuentra archivos que coincidan con `report` o con `summary`. En las consultas OR, todos los términos coincidentes de las ramas que aciertan se resaltan a la vez en el nombre del resultado.

### Precedencia de operadores: AND se une más estrechamente que OR

Cuando se mezclan espacios (AND) y la barra vertical `|` (OR) en una misma consulta, por defecto el espacio se une **más estrechamente** que la barra vertical: cada tramo de términos separados por espacios se combina primero con AND en su propio grupo, y esos grupos se combinan después con OR. No se admiten paréntesis, y esta es la lectura booleana estándar; el orden de agrupación anterior está a un interruptor de distancia (ver más abajo).

```text
report | summary 2024 | draft
```

equivale a `report OR (summary AND 2024) OR draft`.

Esta es la lectura que espera quien escribe varias alternativas y luego acota una de ellas: `summary 2024` se mantiene como una única conjunción en lugar de disolverse en dos alternativas independientes.

#### Volver a OR primero

En **Configuración → General → Sistema → OR se une más fuerte que AND (heredado)** puedes restaurar el orden de agrupación histórico de Lertaro, en el que la barra vertical se une más estrechamente que el espacio:

```text
report | summary 2024 | draft
```

entonces significa `(report OR summary) AND (2024 OR draft)`.

El interruptor solo cambia la precedencia entre los dos operadores: no introduce ningún operador nuevo y no tiene ningún efecto sobre una consulta que use solo uno de ellos (`read me` y `readme | rdm` significan lo mismo con cualquiera de los dos ajustes).

Nota: `|` debe ser un token independiente con espacios a ambos lados — `a|b` o `a |b` no se interpretan como OR. Esto también se aplica a una exclusión: `b | :c` se lee como «b, o no c», así que para excluir algo de toda la consulta, dale a la exclusión su propia posición separada por espacios (`b :c`).

#### La precedencia no se ve afectada por las comillas

No existe ninguna sintaxis de comillas que cambie cómo se lee la precedencia; la agrupación la fija únicamente el ajuste anterior. Una `|` solo es un OR cuando es un token independiente rodeado de espacios, así que incluirla en texto normal como `data|backup` solo busca esa cadena literal.

### La regla del espacio

No hay forma de meter un espacio dentro de un solo término. El espacio es siempre el separador AND, así que una consulta con espacios es siempre varios términos unidos por AND: este es el único punto en el que Lertaro lee la puntuación como estructura:

```text
final report
```

es `final` AND `report`, que no es lo mismo que una única frase `final report`.

**Ni las comillas ni la barra invertida lo resuelven.** `'final report'` y `"final report"` no son sintaxis de frase: las comillas se comparan como caracteres literales, así que esas consultas buscan nombres que contengan una comilla y no encuentran nada. `final\ report` también se lee como las dos palabras `final` AND `report`.

En resumen, Lertaro no tiene búsqueda de frase exacta. Cuando las dos palabras son adyacentes en el nombre que buscas, busca solo la mitad más distintiva y deja que la ordenación la ponga arriba; o usa una cláusula de expresión regular, que compara el nombre como un todo (ver más abajo):

```text
/^final report/
```

### Pegar varias líneas dobladas en OR

Al copiar texto de varias líneas (como nombres de archivo de una hoja de cálculo, un archivo de texto o un registro) y pegarlo directamente en la barra de búsqueda, Lertaro dobla automáticamente las líneas en una única consulta OR separada por `|` (las líneas en blanco se omiten automáticamente):

```text
123
456
678
```

Se pega automáticamente como:

```text
123 | 456 | 678
```

## 3. Tabla resumen de operadores de búsqueda

### Tabla de operadores

| Operador / Sintaxis | Tipo | Descripción | Ejemplo de entrada |
| :--- | :--- | :--- | :--- |
| *(ninguno)* | Término predeterminado | Difuso cuando la coincidencia difusa está activada; subcadena exacta cuando está desactivada | `report` |
| `:` | Exclusión | Descarta todos los resultados cuyo nombre contenga este texto (siempre exacto, nunca difuso) | `:temp` |
| `\|` | Lógica OR | Coincide con cualquiera de los lados de la barra vertical | `doc \| pdf` |
| `/.../` | Expresión regular | Coincide con el nombre mediante una expresión regular de .NET (ver más abajo) | `/^report.*\.md$/` |
| `*` | Omitir exclusiones | Excepción puntual a tus reglas de exclusión configuradas (solo como primer carácter) | `*node_modules` |
| `<` `>` | Token de orden / filtro | Ordena y, opcionalmente, filtra los resultados (ver la [sección 5](#_5-tokens-de-consulta-ordenacion-y-filtrado)) | `<s>20m` |
| `\` | Token de plugin | Aplica un filtro proporcionado por un plugin, p. ej. una categoría de archivo (ver la [sección 5](#_5-tokens-de-consulta-ordenacion-y-filtrado)) | `\audio` |

### Comportamiento detallado de operadores y combinaciones

1. **Exclusión `:`** — `:term` descarta todos los resultados cuyo nombre contenga `term`. La exclusión se escribe **sin espacio** después de los dos puntos (`:temp`, no `: temp`), y no puede ser lo único que haya en la consulta: como una exclusión solo puede quitar resultados, una consulta formada únicamente por exclusiones no muestra ningún resultado. En la práctica, eso significa mantener siempre al menos un término normal junto a ella.
2. **Las exclusiones son siempre exactas** — se comparan como una subcadena contigua incluso con la coincidencia difusa activada, y no se expanden mediante alias de pinyin. De lo contrario, una subsecuencia laxa o una grafía en pinyin eliminaría archivos que nunca nombraste.
3. **Unos dos puntos solos se ignoran** — `:` sin nada detrás no es un operador; simplemente se descarta de la consulta.
4. **Los dos puntos de unidad son distintos** — una letra de unidad va después de los dos puntos (`d:`, ver la [sección 4](#_4-modo-de-ruta-y-delimitacion-por-unidad)), mientras que una exclusión va antes (`:temp`). Es imposible confundirlos, y unos dos puntos dentro de una palabra (`c:\path`) son texto normal.
5. **Una cláusula regex se extrae antes de que nada más lea la consulta** — eso es lo que evita que sus barras invertidas y sus barras se confundan con una ruta, y significa que una cláusula puede situarse en cualquier parte de la consulta (`/\.md$/ report` y `report /\.md$/` son la misma búsqueda).

**Ejemplos de combinación de operadores**:

- `report :draft`: Encuentra los nombres que contienen `report` y descarta cualquiera cuyo nombre contenga `draft`.
- `IMG :png :gif`: Encuentra los nombres que contienen `IMG` y descarta tanto los archivos `png` como los `gif`. Ambas exclusiones se combinan con AND: un nombre solo sobrevive si no contiene ninguna de las dos.
- `log :temp :bak`: Conserva los archivos `log` que no son ni temporales ni copias de seguridad.

### Expresiones regulares (`/.../`)

Escribe una expresión regular de .NET entre barras para que coincida con un **nombre** de archivo exactamente como lo describes:

```text
/^report.*\.md$/
```

Eso encuentra los nombres que empiezan por `report` y terminan en `.md`. La cláusula se combina con AND con el resto de la consulta, así que `report /\.pdf$/` conserva solo los PDF entre las coincidencias de `report`.

Hay cuatro cosas que conviene saber:

- **Coincide con el nombre, no con la ruta**, y tampoco con el contenido del archivo. Usa una consulta de ruta para las carpetas.
- **No pasa por los alias de pinyin.** Una expresión regular describe los caracteres que hay realmente en el nombre, así que un nombre en chino coincide por sus propios caracteres, no por una grafía en pinyin de ellos.
- **Las barras son el delimitador, y `\` escapa.** Escribe `\.` para un punto literal (`.` por sí solo significa cualquier carácter). Para que coincida con una barra literal, escribe `\/`. Como `/` es también el separador de ruta alternativo de Windows, una cláusula tiene que ser una palabra completa que abra y cierre con `/`, y una `/` sin escapar dentro de ella cierra la cláusula: eso es lo que mantiene una ruta con barras como `C:/Users/me`, `/mnt/c/Users` o `/usr/local/` como una ruta normal en lugar de una regex. Una cláusula sin cerrar se trata como texto normal en lugar de tragarse el resto de la consulta.
- **Una expresión regular no se puede acelerar como un término**, porque no es una cadena fija. Lertaro extrae la tirada más larga de caracteres literales que la expresión exige — `.exe` de `/\.exe$/`, nada en absoluto de `/^(ogg|mp3)$/` — y la usa para saltarse la mayoría de los candidatos antes de ejecutar la expresión real. Añadir una palabra normal junto a una expresión regular sin literales es la forma fiable de mantener rápida ese tipo de búsqueda.

## 4. Modo de ruta y delimitación por unidad

### Limitar la búsqueda a una unidad

Empieza la consulta con una letra de unidad seguida de dos puntos para restringir los resultados estrictamente a esa unidad:

```text
d: report
```

La especificación de unidad debe ser un **token independiente de dos caracteres**: después de los dos puntos tiene que haber un espacio. `d:report` ya no es una especificación de unidad, sino un término normal que busca el texto literal `d:report`, porque adivinar la unidad a partir de los dos primeros caracteres producía falsos positivos. Solo se reconocen letras ASCII, así que `中: x` tampoco es una unidad.

### Modo de ruta completa

Cuando la consulta de búsqueda contiene separadores de ruta (`\` o `/`), Lertaro cambia automáticamente al modo de coincidencia de ruta completa:

```text
D:\Projects\Lertaro
```

Si termina con un separador de ruta (p. ej. `D:\Projects\`), busca el contenido directo **dentro** de esa carpeta.

> [!NOTE]
> Un token de plugin también empieza por `\` (por ejemplo, `\audio`). Los tokens se extraen de la consulta antes de decidir el modo de ruta, así que una consulta que solo contenga tokens y palabras normales (`report \audio`) sigue siendo una búsqueda por nombre habitual. El modo de ruta solo se activa cuando queda un separador en el texto restante.

### Coincidencia alternativa en carpetas superiores

Cuando la búsqueda solo por nombre de archivo no llena la capacidad de resultados, Lertaro usa automáticamente los términos de la consulta que no coincidieron en el nombre del archivo para buscar en los nombres de las carpetas superiores, sin necesidad de sintaxis especial:

```text
d01j dcj
```

Aunque `dcj` no aparezca nunca en el propio nombre del archivo, Lertaro encuentra `d01j.txt` ubicado en una carpeta llamada (o con alias) `dcj`.

> [!NOTE]
> Esto requiere que al menos un término coincida con el propio nombre del archivo, y solo se activa cuando las coincidencias por nombre no han llenado los resultados. Los resultados alternativos siempre se ordenan después de las coincidencias directas por nombre.

## 5. Tokens de consulta: ordenación y filtrado

Los tokens de consulta son las palabras que empiezan por un carácter que no puede aparecer en un nombre de archivo de Windows, por lo que nunca se pueden confundir con el texto que quieres buscar. Hay dos familias:

| Familia | Desencadenante | Ejemplo | Propiedad de |
| :--- | :--- | :--- | :--- |
| Orden / filtro | `<` y `>` | `<s>20m` | El plugin `CoreExtensions` |
| Tokens de plugin | Tu **Prefijo de tokens de consulta de plugins** configurado (por defecto `\`) | `\audio` | El plugin que reclame el token |

### Los tokens funcionan en cualquier parte de la consulta

Un token **no** tiene que ir al final. Todas estas consultas son equivalentes:

```text
report <s>20m \audio
<s>20m report \audio
\audio report <s>20m
```

Un token solo cuenta cuando es la **palabra completa** y empieza la palabra: `abc\def` es texto normal, no un token.

### Ordenación y umbrales (`<` y `>`)

El primer carácter elige la dirección de ordenación y la letra siguiente, la propiedad:

| Token | Significado |
| :--- | :--- |
| `<s` | Ordenar por tamaño, los más pequeños primero |
| `>s` | Ordenar por tamaño, los más grandes primero |
| `<c` / `>c` | Ordenar por fecha de creación, los más antiguos / más recientes primero |
| `<m` / `>m` | Ordenar por fecha de modificación, los más antiguos / más recientes primero |
| `<a` / `>a` | Ordenar por fecha de acceso, los más antiguos / más recientes primero |
| `<f` / `>f` | Carpetas primero / archivos primero |

Añade un segundo desencadenante más un umbral para conservar solo un lado:

| Token | Significado |
| :--- | :--- |
| `<s>20m` | Archivos de más de 20 MB |
| `<s<20m` | Archivos de menos de 20 MB |
| `>c>2008.8.3` | Elementos creados después del 2008-08-03 |

El segundo desencadenante es una **comparación**, no una repetición de la flecha de ordenación: `>` siempre significa un límite inferior y `<` siempre significa un límite superior.

Los tamaños aceptan los sufijos `k`, `m`, `g` y `t` (unidades binarias, así que `1m` es 1 MiB) o un número de bytes sin más. Los umbrales de la clave carpeta/archivo usan `f` / `folder` / `dir`.

Las fechas deben escribirse **primero el año**. Se aceptan estas formas (un año de dos dígitos se lee como `20xx`):

| Separador | Ancho del año | Ancho de mes/día | Ejemplos |
| :--- | :--- | :--- | :--- |
| `-` | 4 o 2 | con relleno o sin él | `2003-01-03`, `2003-1-3`, `03-1-3` |
| `.` | 4 o 2 | con relleno o sin él | `2003.01.03`, `2003.1.3`, `03.1.3` |
| `/` | 4 o 2 | con relleno o sin él | `2003/01/03`, `2003/1/3`, `03/1/3` |
| ninguno | 4 | — | `20030103` |

Los separadores no se pueden mezclar (`2003-08.03` no es una fecha), y no se acepta la lectura con el mes primero `01-03-2003`: el orden es siempre año, mes, día, de modo que una consulta nunca puede significar dos días distintos en dos máquinas. También se aceptan las formas solo con año (`2008`) y año-mes (`2008.8`), que significan «durante 2008» y «durante agosto de 2008».

### Tokens de plugin (`\`)

Los tokens de plugin los proporcionan los plugins, y cada plugin decide qué significa cada uno. El plugin integrado `CoreExtensions` incluye filtros por categoría de archivo:

- `\doc`: Documentos (`*.doc; *.docx; *.pdf; *.txt; *.ppt; *.pptx; *.xls; *.xlsx; *.csv; *.rtf; *.md; *.wps`)
- `\img`: Imágenes (`*.jpg; *.jpeg; *.png; *.gif; *.bmp; *.webp; *.ico; *.svg; *.tif; *.tiff; *.psd; *.ai`)
- `\video`: Vídeos (`*.mp4; *.mkv; *.avi; *.mov; *.wmv; *.flv; *.m4v; *.webm; *.3gp; *.rmvb; *.ts`)
- `\audio`: Audio (`*.mp3; *.wav; *.flac; *.aac; *.ogg; *.m4a; *.wma; *.ape`)
- `\zip`: Archivos comprimidos (`*.zip; *.rar; *.7z; *.tar; *.gz; *.bz2; *.xz; *.iso; *.wim; *.esd`)

**Ejemplos**:

- `financial \doc`: Busca «financial» entre los documentos.
- `wallpaper \img`: Busca «wallpaper» entre las imágenes.

Puedes renombrar las categorías, cambiar las extensiones que cubre cada una o añadir las tuyas en **Configuración → Plugins → CoreExtensions**. La propia palabra clave se compara de la más larga a la más corta, así que una regla `\a` y una regla `\audio` pueden coexistir y `\audio` sigue ganando.

El carácter de prefijo se configura en **Configuración → General → Sistema → Prefijo de tokens de consulta de plugins**. No puede estar vacío, no puede ser `<` ni `>`, y no puede ser el mismo carácter que el prefijo propio de otro plugin: los ajustes informan de esa colisión en lugar de dejar que un proveedor gane en silencio.

La barra lateral de filtros de tipo de la ventana de búsqueda completa se configura por separado en el grupo **Filtros de búsqueda** del mismo plugin. Los nombres de los filtros de la barra lateral solo sirven para mostrar; las referencias de prefijo solo se analizan dentro de una regla de filtro de la barra lateral y se refieren a palabras clave de la lista **Filtros personalizados**, incluidos los filtros personalizados deshabilitados.

### Tokens encadenados

Como los tokens son palabras independientes, pueden aparecer varios en una misma consulta y cada uno se aplica por turno:

- `report \doc >m`: Busca «report», conserva solo los documentos y ordena por fecha de modificación (los más recientes primero).
- `backup \zip <s`: Busca «backup», conserva los archivos `.zip` y ordena por tamaño (los más pequeños primero).
- `icon \img <s>1m`: Busca «icon», conserva las imágenes de más de 1 MB, las más pequeñas primero.

## 6. Funciones especiales de búsqueda

### Omitir las reglas de exclusión en una sola búsqueda

Antepón `*` a una consulta para omitir temporalmente las rutas excluidas, los globs y las expresiones regulares configuradas por el usuario en [**Reglas de exclusión**](./settings/index-drives#_5-reglas-de-exclusion) durante esa única búsqueda, sin modificar la configuración:

```text
*node_modules
```

El `*` inicial se elimina antes de la coincidencia. Esto solo recupera archivos que ya estén indexados (las rutas excluidas de unidades de red o WSL que nunca se indexaron no aparecerán); los filtros de archivos del sistema y ocultos siguen activos. Ten en cuenta que se trata de las **reglas** de exclusión de la propia aplicación, algo distinto de un término de exclusión `:` en la consulta.

### Activador de tipo de resultado

En **Configuración → General → Ventana de búsqueda rápida → Prioridad de tipo de resultado** puedes configurar un **activador** de un solo carácter para tipos de resultado concretos (Aplicaciones, Configuración, Categorías de archivo, Plugins, Archivos, etc.).

Escribir el activador como el primer carácter en la ventana de búsqueda rápida muestra únicamente ese tipo de resultado y oculta todos los demás:

```text
;vs
```

Si `;` está asignado a «Aplicaciones», la consulta anterior busca Visual Studio exclusivamente entre las aplicaciones. El activador debe ser el primer carácter, sin nada delante, y solo se aplica a las ventanas de búsqueda rápida e integrada. En ambas, el Historial y los Favoritos permanecen fijados en la parte superior independientemente de los activadores.

> [!NOTE]
> El activador debe ser el primer carácter de la consulta: un token de plugin o de ordenación antes de él (`\img ;vs`) deja el activador sin leer, ya que la consulta ya no empieza por él.

## 7. Alias multilingües

### Nombres de archivo en chino: alias de pinyin

Incluido con el plugin `PinyinAlias`, los nombres de archivo en chino se pueden buscar por pinyin directamente y sin ninguna configuración:

- **Pinyin completo**: Escribir `chongqing` coincide con `重庆.docx`.
- **Iniciales de pinyin**: Escribir `cq` también coincide con `重庆.docx`; escribir `wzry` coincide con `王者荣耀.exe`.
- **Caracteres polifónicos**: Las pronunciaciones habituales se indexan automáticamente (p. ej. `重庆` coincide tanto con `chongqing` como con `zhongqing`).

Puedes comprobar que `PinyinAlias` está activo en **Configuración → Plugins**.

### Nombres de archivo en español: alias de acentos

Incluido con el plugin `SpanishAlias`, los nombres de archivo que contienen caracteres acentuados del español (`á`, `é`, `í`, `ó`, `ú`, `ü`, `ñ`) se pueden buscar sin problemas con letras ASCII sin acentos:

- Escribir `cancion` coincide con `Canción.mp3`.
- Escribir `nino` coincide con `Niño.txt`.
- Escribir `ciguena` coincide con `Cigüeña.png`.

Los caracteres coincidentes (incluidas las vocales acentuadas del nombre original) se resaltan con precisión. Gestiona el plugin en **Configuración → Plugins**.

## 8. Preguntas frecuentes y favoritos

### Favoritos, no alias personalizados

Lertaro no ofrece un mecanismo genérico de «alias/macros de búsqueda personalizados». Las soluciones nativas más cercanas:

- [**Favoritos**](./settings/favorites): fija cualquier archivo, carpeta o URL con un nombre de visualización personalizado, lo que lo hace buscable por ese título personalizado (marcado con un icono ★ en los resultados).
- **Filtros de archivos** (ver [**Respuestas instantáneas**](./instant-answers#_5-filtros-de-archivos)): vincula una palabra clave a las carpetas que elijas y, al escribir `keyword term` en la ventana de búsqueda rápida, se restringe una búsqueda normal del índice a esas carpetas.

Si quieres activar scripts personalizados o lanzar programas con palabras clave personalizadas, consulta [**Comandos personalizados**](./instant-answers#_6-comandos-personalizados).
