# Sintaxis de búsqueda

La barra de búsqueda de Lertaro admite mucho más que una simple búsqueda de texto plano. Equipada con un algoritmo de coincidencia ultrarrápido, admite coincidencia difusa con salto de caracteres, operadores lógicos, modificadores de límite de palabra, delimitación por unidad y ruta, fichas de consulta (Query Tokens) para filtrado secundario y alias multilingües inteligentes. Todas las sintaxis se pueden combinar libremente en la misma consulta.

## 1. Modos de coincidencia básica y distinción entre mayúsculas y minúsculas

### Coincidencia difusa (predeterminada)

Lertaro activa la coincidencia difusa (Fuzzy Matching) de forma predeterminada. Simplemente escribe cualquier parte de las palabras y coincidirá siempre que los caracteres aparezcan en orden en el nombre del archivo o carpeta, incluso si no son continuos:

| Ejemplo de entrada | Resultado de coincidencia | Descripción |
| :--- | :--- | :--- |
| `ltro` | `Lertaro.exe` | Los caracteres coinciden en orden: `l` → `t` → `r` → `o` (**L**er**t**a**ro**.exe) |
| `vsc` | `Visual Studio Code.lnk` | Coincide con las iniciales de cada palabra (**V**isual **S**tudio **C**ode) |
| `rt-fin` | `Q3-report-final.docx` | Coincide con la subcadena continua (Q3-repo**rt-fin**al.docx) |

Desactiva esta opción en **Configuración → General → Sistema → Habilitar coincidencia difusa** y los términos de búsqueda simples (sin operadores) requerirán una subcadena continua — `abc` solo coincidirá con nombres que contengan `abc` continuo, ya no con `a-b-c`. Esta opción solo afecta a los términos simples; todos los operadores descritos a continuación mantienen su comportamiento exacto.

### Distinción inteligente de mayúsculas y minúsculas (Smart Case)

- **Consultas en minúsculas**: No distinguen entre mayúsculas y minúsculas por defecto. Por ejemplo, `myfile` coincide con `myfile`, `MyFile`, `MYFILE`, etc.
- **Consultas con letras mayúsculas**: Una vez que un término contiene cualquier letra mayúscula, se vuelve automáticamente **sensible a las mayúsculas**. Por ejemplo, `MyFile` solo coincide con `MyFile`, y no con `myfile` en minúsculas.

## 2. Varios términos y operadores lógicos

### Espacio: AND (Y)

Separa varios términos de búsqueda con espacios para exigir que se cumplan todas las condiciones. El orden en el que aparecen los términos en el nombre del archivo **no importa**:

```text
report final 2024
```

La consulta anterior coincide tanto con `2024-Q3-report-final.docx` como con `final_report_2024.pdf`.

### Barra vertical `|`: OR (O)

Usa el símbolo de barra vertical `|` para separar términos cuando baste con que coincida cualquiera de las alternativas:

```text
png | jpg | gif
```

Puedes combinar libremente la lógica AND y OR:

```text
report | summary 2024
```

Esto busca archivos que contengan `report` o `summary`, y que además contengan `2024`. En las consultas OR, todos los términos que coincidan se resaltarán simultáneamente en el nombre del resultado.

### Precedencia de operadores: OR se agrupa más estrechamente que AND

Cuando se mezclan espacios (AND) y la barra vertical `|` (OR) en una misma consulta, `|` tiene **mayor** prioridad que los espacios: los términos de ambos lados de `|` se agrupan primero en un único grupo OR, y los grupos separados por espacios se combinan después con AND. No se admiten paréntesis, por lo que este orden de agrupación no se puede cambiar.

```text
report | summary 2024 | draft
```

equivale a `(report OR summary) AND (2024 OR draft)`.

Nota: `|` debe ser un token independiente con espacios a ambos lados: `a|b` o `a |b` no se interpreta como OR. Tampoco metas un término de exclusión `!` dentro de un grupo OR (p. ej. `b | !c`), que se interpreta como «b coincide o c no coincide»; para excluir un término globalmente, dale su propia condición AND separada por espacios (p. ej. `b !c`).

### Espacios en términos y frases entre comillas

Para buscar una frase que contenga espacios dentro de un solo término, escapa el espacio con una barra invertida `\ `, o encierra la frase entre comillas simples `'...'` o dobles `"..."`:

```text
final\ report
'final report'
```

Ambas formas tratan `final report` como una frase única con espacio, en lugar de dividirla en dos términos AND independientes.

### Pegar texto de varias líneas doblado en OR

Al copiar texto de varias líneas (por ejemplo, nombres de archivo de una hoja de cálculo, archivo de texto o registro) y pegarlo directamente en el cuadro de búsqueda, Lertaro dobla automáticamente las líneas en una única consulta OR separada por `|` (las líneas en blanco se omiten automáticamente):

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

| Operador / Sintaxis | Tipo | Descripción | Ejemplo de entrada | Ejemplo de coincidencia |
| :--- | :--- | :--- | :--- | :--- |
| *(ninguno)* | Difuso predeterminado | Los caracteres aparecen en orden en cualquier parte del nombre | `report` | `Q3-report-final.docx` |
| `!` | Excluir | Excluye todos los resultados cuyo nombre contenga esta subcadena exacta | `!temp` | Filtra archivos que contengan `temp` |
| `'` | Invertir exactitud | Subcadena exacta si el modo difuso está activo; difuso si está apagado | `'report` | Debe contener la subcadena continua `report` |
| `'...'` | Límite de palabra | Subcadena exacta en límites de palabra (no dentro de palabras más largas) | `'app'` | Coincide con `app.exe`, `my-app.log`; no con `whatsapp.exe` |
| `^` | Coincidencia de prefijo | El nombre debe comenzar con este texto | `^IMG` | `IMG_20240101.jpg` (no coincide con `MY_IMG.jpg`) |
| `$` | Coincidencia de sufijo | El nombre debe terminar con este texto | `.pdf$` | `document.pdf` (no coincide con `document.pdf.bak`) |
| `^...$` | Coincidencia exacta | El nombre debe ser exactamente igual a este texto | `^readme.md$` | Coincide únicamente con `readme.md` |
| `\|` | Lógica OR | Coincide con cualquiera de los lados de la barra | `doc \| pdf` | Coincide con nombres que contengan `doc` o `pdf` |

### Comportamiento detallado de operadores y combinaciones

1. **Exclusión `!`**: `!term` descarta directamente los resultados que contengan `term` como subcadena exacta. Los términos excluidos no se expanden con pinyin ni alias para evitar exclusiones accidentales.
2. **Inversión de exactitud `'`**: Cuando la coincidencia difusa global está activada, anteponer `'` fuerza a un término específico a coincidir como subcadena continua exacta.
   - Por ejemplo, `lertaro 'v1.2` busca `lertaro` de forma difusa mientras que exige `v1.2` de forma continua y exacta.
   - Los términos exactos siguen coincidiendo con nombres de archivo que tienen alias en pinyin: `'exe$` también encuentra `古恩希尔`, cuyo pinyin se asigna a `gexe`.
3. **Límite de palabra `'...'`**: Encerrar una palabra entre comillas (por ejemplo, `'app'`) verifica los límites anteriores y posteriores (espacios, signos de puntuación, guiones, guiones bajos o extremos de cadena), evitando falsos positivos dentro de palabras largas.
4. **Coincidencia exacta `^...$`**: Solo se aplica cuando `^` y `$` envuelven la **misma palabra**. Si se escriben en palabras separadas (por ejemplo, `^src md$`), siguen actuando como filtros independientes de prefijo y sufijo.

**Ejemplos de combinación de operadores**:

- `^IMG !.png$ 2024`: Busca archivos que comiencen por `IMG`, contengan `2024` y **no** terminen en `.png`.
- `'data | 'backup ^2024 .zip$`: Busca archivos comprimidos que comiencen por `2024`, terminen en `.zip` y contengan la subcadena exacta `data` o `backup`.
- `^report '公告 | 'gw .pdf$ !draft`: Busca nombres que empiecen por `report`, terminen en `.pdf`, no contengan `draft` y contengan exactamente `公告`, `gw`, `公文` u otras combinaciones que coincidan con el pinyin `gw` (`'公告 | 'gw` forma un grupo OR; el resto de condiciones separadas por espacios se combinan con AND).

## 4. Modo de ruta y delimitación por unidad

### Especificar una unidad

Comienza la consulta con una letra de unidad seguida de dos puntos para limitar los resultados estrictamente a esa unidad:

```text
d: report
```

El espacio es opcional: `d:report` y `d: report` son equivalentes.

### Modo de ruta completa

Cuando la consulta contiene separadores de ruta (`\` o `/`), Lertaro cambia automáticamente al modo de coincidencia de ruta completa:

```text
D:\Projects\Lertaro
```

Si termina con un separador de ruta (por ejemplo, `D:\Projects\`), busca el contenido directo **dentro** de esa carpeta.

### Coincidencia alternativa en carpetas superiores (Folder Matching)

Cuando la búsqueda solo por nombre de archivo no llena la capacidad de resultados, Lertaro utiliza automáticamente los términos no coincidentes para buscar coincidencias en las carpetas superiores sin necesidad de sintaxis especial:

```text
d01j dcj
```

Incluso si `dcj` nunca aparece en el propio nombre del archivo, Lertaro encuentra `d01j.txt` ubicado dentro de una carpeta llamada (o con alias) `dcj`.

> [!NOTE]
> Esto requiere que al menos un término coincida con el nombre del archivo, y solo se activa cuando las coincidencias directas no llenan los resultados. Los resultados de respaldo siempre se ordenan después de las coincidencias directas.

## 5. Fichas de consulta y filtrado secundario (Query Tokens)

Lertaro permite añadir **fichas de consulta (Query Tokens)** encabezadas por dos puntos `:` (personalizable en **Configuración → General → Sistema → Carácter de prefijo global de token de consulta**) al final de la búsqueda para realizar filtrados y ordenaciones secundarias en cadena.

Puedes combinar varias fichas tras un solo prefijo `:` separándolas por comas `,`, como en `report :@doc,M-,:-F`.

### Filtros de categoría de archivo (`:@<categoría>`)

Aplica rápidamente reglas preestablecidas de extensión de archivo, admitiendo combinaciones con `|`:

- `:@doc`: Documentos (`*.doc; *.docx; *.pdf; *.txt; *.ppt; *.pptx; *.xls; *.xlsx; *.csv; *.rtf; *.md; *.wps`)
- `:@img`: Imágenes (`*.jpg; *.jpeg; *.png; *.gif; *.bmp; *.webp; *.ico; *.svg; *.tif; *.tiff; *.psd; *.ai`)
- `:@video`: Vídeos (`*.mp4; *.mkv; *.avi; *.mov; *.wmv; *.flv; *.m4v; *.webm; *.3gp; *.rmvb; *.ts`)
- `:@audio`: Audio (`*.mp3; *.wav; *.flac; *.aac; *.ogg; *.m4a; *.wma; *.ape`)
- `:@zip`: Archivos comprimidos (`*.zip; *.rar; *.7z; *.tar; *.gz; *.bz2; *.xz; *.iso`)

**Ejemplos**:

- `financiero :@doc`: Busca "financiero" entre documentos.
- `wallpaper :@img`: Busca "wallpaper" entre imágenes.
- `clip :@video|audio`: Busca "clip" entre vídeos o archivos de audio.

Puedes personalizar las reglas o añadir nuevas categorías en **Configuración → Plugins → CoreExtensions**.

La barra lateral de filtros de tipo de la ventana de búsqueda completa se configura por separado en el grupo **Filtros de búsqueda** del mismo plugin. Los nombres de los filtros de la barra lateral solo sirven para mostrar texto; las referencias `@palabra-clave` solo se procesan dentro de una regla de filtro lateral y apuntan a palabras clave de la lista **Filtros personalizados**, incluidos los filtros deshabilitados.

### Filtros por extensión específica (`:.ext` o `:.ext1.ext2`)

Usa un punto para especificar una o varias extensiones (excluye carpetas automáticamente):

- `report :.pdf`: Conserva únicamente archivos `.pdf`.
- `data :.csv.xlsx`: Conserva únicamente archivos de hoja de cálculo `.csv` o `.xlsx`.

### Ordenación y filtros de archivo/carpeta (`:[SCMAF]`)

Usa letras individuales para especificar atributos: `S` (Tamaño/Size), `C` (Creación/Created), `M` (Modificación/Modified), `A` (Acceso/Accessed), `F` (Carpeta/Folder).

La letra sin signo indica **orden ascendente** (menor tamaño / más antiguo primero); añadir un signo menos `-` (como prefijo o sufijo, por ejemplo, `M-` o `:-M`) indica **orden descendente** (mayor tamaño / más reciente primero) o filtrado inverso:

| Sintaxis | Efecto | Escenario típico |
| :--- | :--- | :--- |
| `:S` | Ordenar por tamaño ascendente (más pequeños primero) | Localizar archivos vacíos o diminutos |
| `:S-` o `:-S` | Ordenar por tamaño descendente (más grandes primero) | `log :S-` (revisar archivos de registro enormes) |
| `:M` | Ordenar por fecha de modificación ascendente (más antiguos) | Revisar archivos sin actualizar hace mucho |
| `:M-` o `:-M` | Ordenar por fecha de modificación descendente (más recientes) | `report :M-` (encontrar documentos editados recientemente) |
| `:C` / `:C-` | Ordenar por fecha de creación ascendente / descendente | `build :C-` (encontrar las compilaciones más recientes) |
| `:A` / `:A-` | Ordenar por fecha de acceso ascendente / descendente | `project :A-` (encontrar proyectos abiertos recientemente) |
| `:F` | **Solo carpetas** (excluye archivos normales) | `config :F` (buscar solo directorios llamados config) |
| `:-F` o `:F-` | **Solo archivos** (excluye carpetas/directorios) | `config :-F` (buscar solo archivos llamados config) |

### Filtros secundarios con comodines (`:?<expresión>` o `?<expresión>`)

Usa comodines estándar de Windows (`?` para un carácter, `*` para cero o más caracteres) para una coincidencia precisa, admitiendo `|` o `;` para varias condiciones OR:

- `mp4 :?(2026???????????)`: Coincide con archivos de vídeo con `2026` y una marca de tiempo de 11 dígitos.
- `photo :?IMG_????.jpg|DSC_????.jpg`: Coincide con números de foto específicos entre dos formatos de cámara.

### Filtros secundarios por segmento de ruta (`::<expresión>`)

Requiere que los nombres de las carpetas superiores o el propio archivo coincidan con la palabra clave difusa:

- `report ::2024`: Exige que la ruta contenga `2024`.
- `main ::"src\core"`: Limita la búsqueda a archivos ubicados dentro de `src\core` y sus subdirectorios.

### Ejemplos de fichas encadenadas

Las fichas se pueden combinar juntas tras un único prefijo `:`:

- `informe :@doc,M-`: Busca "informe", filtra por documentos y ordena por fecha de modificación descendente (más recientes primero).
- `backup :.zip,S-,:-F`: Busca "backup", filtra por archivos `.zip`, ordena por tamaño de mayor a menor y muestra solo archivos.
- `icon ::assets,?*128*`: Busca "icon", ubicado bajo rutas `assets` y con indicador de tamaño `128` en el nombre.

## 6. Funciones especiales de búsqueda

### Omitir reglas de exclusión en una sola búsqueda

Escribe `*` al principio de la consulta para ignorar temporalmente las rutas excluidas, globs y expresiones regulares configuradas en [**Reglas de exclusión**](./settings/index-drives#reglas-de-exclusion) para esa búsqueda puntual, sin modificar la configuración:

```text
*node_modules
```

El `*` inicial se elimina automáticamente antes de la búsqueda. Solo recupera elementos que ya hayan sido indexados (las carpetas nunca indexadas en unidades de red o WSL no aparecerán); los filtros de archivos ocultos y del sistema permanecen activos.

### Activador de tipo de resultado

En **Configuración → General → Ventana de búsqueda rápida → Prioridad de tipo de resultado**, puedes asignar un **activador** de un solo carácter a tipos de resultado específicos (Aplicaciones, Configuración, Categorías de archivo, Elementos de plugins, Archivos, etc.).

Escribir el activador como el primer carácter en la ventana de búsqueda rápida muestra únicamente los resultados de ese tipo, ocultando todos los demás:

```text
;vs
```

Si `;` está asignado a "Aplicaciones", la consulta anterior buscará Visual Studio exclusivamente entre aplicaciones. En las ventanas rápida e incrustada, el Historial y los Favoritos permanecen fijados en la parte superior independientemente de los activadores.

## 7. Alias multilingües

### Nombres de archivo en chino: alias en pinyin

Gracias al plugin integrado `PinyinAlias`, los nombres de archivo en chino se pueden buscar mediante pinyin sin necesidad de configuración:

- **Pinyin completo**: Escribir `chongqing` coincide con `重庆.docx`.
- **Iniciales de pinyin**: Escribir `cq` también coincide con `重庆.docx`; escribir `wzry` coincide con `王者荣耀.exe`.
- **Caracteres polifónicos**: Las pronunciaciones habituales se indexan automáticamente (por ejemplo, `重庆` coincide con `chongqing` y `zhongqing`).

Puedes verificar que `PinyinAlias` esté activo en **Configuración → Plugins**.

### Nombres de archivo en español: alias de acentos

Con el plugin integrado `SpanishAlias`, los nombres de archivo que contienen caracteres con acento o tilde en español (`á`, `é`, `í`, `ó`, `ú`, `ü`, `ñ`) se pueden buscar directamente usando letras ASCII normales sin acentos:

- Escribir `cancion` coincide con `Canción.mp3`.
- Escribir `nino` coincide con `Niño.txt`.
- Escribir `ciguena` coincide con `Cigüeña.png`.

Los caracteres coincidentes (incluidas las vocales acentuadas en el nombre original) se resaltan con precisión. Gestiona el plugin en **Configuración → Plugins**.

## 8. Preguntas frecuentes y Favoritos

### Favoritos, no alias personalizados

Lertaro no dispone de un sistema genérico de "alias/macros de búsqueda personalizados". Las soluciones nativas más cercanas:

- [**Favoritos**](./settings/favorites): fija cualquier archivo, carpeta o URL con un nombre de visualización personalizado y podrás buscarlo directamente por ese título (marcado con un icono ★ en los resultados).
- **Filtros de archivos** (consulta [**Respuestas instantáneas**](./instant-answers#filtros-de-archivos)): vincula una palabra clave a carpetas concretas y, al escribir `palabraclave término` en la ventana de búsqueda rápida, la búsqueda normal del índice quedará limitada a esas carpetas.

Si deseas ejecutar programas o scripts mediante palabras clave personalizadas, consulta [**Comandos personalizados**](./instant-answers#comandos-personalizados).
