# Ejemplos de plugins

Para ayudar a los desarrolladores a comprender cómo interactúan las interfaces de `Lertaro.PluginSdk`, este capítulo analiza tres plugins representativos incluidos en el repositorio de Lertaro.

## 1. CoreExtensions —— Acciones, menús Shell y Panel rápido

El plugin `CoreExtensions` constituye el paquete de extensiones principal de Lertaro e implementa `IPlugin`, `IActionProvider`, `IConfigurable` y varios proveedores secundarios.

### Puntos clave de implementación

- **Acciones estáticas (`IActionProvider.GetActions()`)**: Registra 10 acciones esenciales sobre archivos (Abrir, Ubicar en Explorador, Copiar ruta, Copiar/Cortar archivos, Abrir consola y Ejecutar como Administrador).
- **Integración con menús Shell de Windows (`IDynamicActionProvider`)**: Se comunica con las interfaces COM del Shell mediante `ShellMenuActionProvider`, renderizando menús contextuales completos (con submenús como "Enviar a", 7-Zip, VS Code) dentro del menú `Ctrl+O`.
- **Formularios de configuración por esquemas (`IConfigurable`)**: Define esquemas con grupos anidados (`Group`), listas de cadenas (`StringList`) y asignación de atajos (`Hotkey`), generando formularios nativos en Configuración sin escribir XAML.
- **Pestañas para el Panel rápido (`IQuickPanelTabProvider`)**:
  - `FavoritesTabProvider` / `HistoryTabProvider`: Devuelve colecciones en memoria con cero coste de E/S.
  - `WindowsRecentTabProvider`: Recorre la carpeta `Recent` en segundo plano, resuelve accesos directos COM y añade `Metadata.Modified` para su ordenación.
  - `LastDirectoryTabProvider` / `RecentFilesTabProvider`: Consulta directamente [`ExplorerPathService`](./sdk/services) y `RecentFilesService` del anfitrión.

## 2. PinyinAlias —— Motor de transliteración para caracteres no ASCII

`PinyinAlias` proporciona búsqueda por pinyin completo y siglas para nombres de archivos en chino, implementando `IAliasProvider` e `ITranslationProvider`.

### Puntos clave de implementación

- **Límites de alfabetos (`InputRanges` / `OutputRanges`)**: Declara los bloques ideográficos CJK como entrada y `a`–`z` en minúsculas como salida, permitiendo dividir consultas mixtas en segmentos literales y de alias.
- **Comprobación previa rápida (`CanHandle(text)`)**: Detecta si el texto contiene caracteres chinos antes de generar alias, devolviendo `false` inmediatamente para cadenas en inglés.
- **Combinaciones polifónicas (`GetAliases(text)`)**: Construye un mapa silábico y genera combinaciones unidas por `|` (hasta un máximo de 32 para evitar saturación), permitiendo búsquedas paralelas.
- **Localización incrustada y caché segura**: Traduce el nombre del plugin mediante `ITranslationProvider` y almacena los JSON analizados en un diccionario protegido con `lock`.

## 3. FlowLauncherBridge —— Compatibilidad entre ecosistemas y entornos aislados

El plugin `FlowLauncherBridge` demuestra la creación de un sistema de puente a gran escala para integrar plugins de la comunidad Flow Launcher.

### Puntos clave de implementación

- **Puente multiproceso y multilenguaje**: Ejecuta plugins escritos en C# (.NET), Python 3.12, Node.js v20 LTS y binarios `.exe`.
- **Entornos autónomos aislados**: Despliega entornos de Python y Node.js en la carpeta de datos de Lertaro y se comunica mediante tuberías con nombre usando JSON-RPC sin modificar la variable PATH.
- **Formularios dinámicos y vistas previas en WebView2**: Asigna formularios `SettingsTemplate.yaml`/`.json` a `PluginConfigSchema` y renderiza tarjetas interactivas (diccionarios, tiempo, etc.) dentro de QuickLook.
