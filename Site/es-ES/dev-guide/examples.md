# Plugins de ejemplo

Dos plugins se distribuyen con el propio Lertaro y son referencias útiles del mundo real — ambos residen en la
carpeta `Plugins/` del repositorio de Lertaro.

## CoreExtensions — acciones y el menú contextual del shell

`CoreExtensionsPlugin` implementa tres interfaces a la vez: `IPlugin`, `IActionProvider` e
`IConfigurable`.

- **`IActionProvider.GetActions()`** devuelve diez `ISearchResultAction` integradas — abrir, localizar en
  el Explorador, copiar ruta, copiar/cortar el propio archivo, abrir un símbolo del sistema en su ubicación, crear archivo/carpeta,
  y variantes elevadas (ejecutar como administrador) de abrir y símbolo del sistema.
- **`IActionProvider.GetDynamicActionProviders()`** devuelve un único `IDynamicActionProvider` —
  `ShellMenuActionProvider` — que es lo que hace que el menú contextual real de Windows (incluidos
  submenús en cascada anidados como "Enviar a") aparezca dentro del propio menú de Acciones de Lertaro. Este es el
  patrón que hay que copiar si quieres mostrar *cualquier* menú externo generado dinámicamente dentro de Lertaro
  en lugar de una lista fija de acciones.
- **`IConfigurable.GetConfigSchema()`** muestra un esquema de configuración con grupos de campos anidados y un
  tipo de campo `StringList` — merece la pena leerlo si tu propio plugin necesita algo más que una lista plana de
  booleanos en su cuadro de configuración de Configuración → Plugins.
- Cinco proveedores implementan
  [`IQuickPanelTabProvider`](./sdk/ui-extensions#iquickpaneltabprovider), y entre ellos cubren ambos
  extremos de esa interfaz. `FavoritesTabProvider` y `HistoryTabProvider` devuelven tal cual una lista que
  ya está en memoria — la referencia mínima, ya que ninguno lleva estado propio. `WindowsRecentTabProvider`
  es el otro extremo: lee un directorio y resuelve accesos directos del shell por COM en una tarea en
  segundo plano, recorta el conjunto *antes* de hacer la parte cara, y rellena el `Metadata.Modified` de
  cada entrada para que el orden por lo más nuevo de la pestaña signifique algo.
- `LastDirectoryTabProvider` y `RecentFilesTabProvider` merecen leerse por otro motivo: ninguno tiene datos
  propios. Se los piden al anfitrión a través de [`ExplorerPathService`](./sdk/services) y
  `RecentFilesService`, que es el patrón a copiar siempre que lo que tu plugin quiere mostrar sea algo que
  Lertaro ya sabe.

## PinyinAlias — alias en pinyin para nombres de archivo en chino

`PinyinAliasProvider` implementa tanto `IAliasProvider` como `ITranslationProvider` — un plugin puede
combinar libremente roles del SDK cuando están relacionados, y este es un buen modelo para ello:

- **`IAliasProvider.InputRanges`/`OutputRanges`** declaran sus dos alfabetos directamente a partir de
  los límites de la propia tabla de `PinyinEngine` (`InputRanges`: el bloque CJK; `OutputRanges`: `a`-`z`) en lugar de
  duplicar números mágicos — el host usa ambos juntos para admitir consultas mixtas de literal+pinyin como `大cj`
  contra `大长今`.
- **`IAliasProvider.CanHandle(text)`** busca cualquier carácter chino antes de hacer trabajo real,
  de modo que los nombres de archivo que no son chinos se saltan por completo la generación de alias.
- **`IAliasProvider.GetAliases(text)`** construye una tabla de sílabas por carácter (cada carácter
  chino se asocia a sus posibles lecturas en pinyin) y luego genera tanto un alias en pinyin completo como un
  alias solo de iniciales. Para nombres de archivo con caracteres polifónicos (más de una lectura válida), genera
  alias para cada combinación habitual — limitado a 32 combinaciones para evitar una explosión
  combinatoria con entradas patológicas — uniendo las alternativas con `|` para que el motor de búsqueda
  trate cada una como candidata en lugar de exigir que todas coincidan simultáneamente.
- **`ITranslationProvider`** se implementa en la *misma* clase, únicamente para proporcionar las cadenas de interfaz
  propias de este plugin (por ejemplo, su nombre visible) mediante `TranslationService.LoadEmbeddedTranslations` — las dos
  interfaces no están relacionadas en propósito, pero coinciden aquí en un mismo tipo por tratarse de un plugin
  pequeño, de un solo archivo.
- Una caché `Dictionary<string, Dictionary<string, string>>` protegida por un `lock` evita volver a analizar
  el JSON de traducción incrustado en cada llamada — el patrón habitual para cualquier plugin que realice
  trabajo no trivial en `GetTranslations`.

Leer ambos plugins uno junto al otro es la forma más rápida de ver cómo encajan en la práctica las piezas de la
[Referencia del SDK de Plugins](./sdk/core-search-actions).
