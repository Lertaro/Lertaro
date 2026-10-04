# Empaquetado y distribución

Este capítulo detalla las convenciones de carpetas para los ensamblados de plugins, la inclusión de librerías dependientes, el empaquetado de recursos de traducción JSON y el flujo de despliegue automatizado.

## 1. Estructura de carpetas de plugins

Lertaro escanea recursivamente la carpeta `Plugins\` ubicada en la raíz de la aplicación. Para evitar conflictos entre dependencias de distintos plugins, se recomienda aislar cada plugin en su propia subcarpeta:

```text
Lertaro/
├── Lertaro.App.exe
├── Lertaro.PluginSdk.dll
└── Plugins/
    └── MyCustomPlugin/
        ├── Lertaro.Plugins.MyCustomPlugin.dll   (Ensamblado principal)
        ├── ThirdParty.Managed.dll               (Dependencia administrada)
        └── NativeLibrary.dll                    (Dependencia nativa C/C++ — colocada EN PLANO)
```

- **Resolución automática de dependencias**: Al cargar la DLL principal mediante `Assembly.LoadFrom`, el entorno de .NET resuelve automáticamente las dependencias adyacentes sin interferir con otros plugins.
- **Las dependencias nativas van junto a la DLL, no bajo `runtimes\<rid>\native`**: los proyectos de plugin incluidos en el repositorio fijan `<GenerateDependencyFile>false</GenerateDependencyFile>`, así que no se genera ningún `.deps.json` y el entorno no dispone de un grafo de dependencias desde el que resolver una subcarpeta de RID. Una librería nativa tiene que poder encontrarse, por lo tanto, en el directorio base de la aplicación: cópiala en plano. También es la razón de que las dos arquitecturas se publiquen como artefactos separados y no como un instalador único: una sola copia nativa, la de una arquitectura, puede ocupar ese directorio de carga plano.
- **Tolerancia a archivos nativos**: Cuando el escaneo de ensamblados encuentra binarios nativos (p. ej. `e_sqlite3.dll`), el cargador los registra como `Debug` y sigue adelante en lugar de lanzar un `Error` falso positivo.

## 2. Configuración de copia automática PostBuild

Añade un destino `PostBuild` en el archivo `.csproj` del plugin para desplegar la salida en los directorios de depuración de Lertaro tras cada compilación exitosa. Esto es lo que hacen realmente los proyectos de plugin del repositorio — fija el destino **en plano** y la **segunda copia para el Servicio**, que es lo que deja un proveedor de alias o de traducción disponible tanto para el indexador como para la interfaz:

```xml
<Target Name="PostBuild" AfterTargets="PostBuildEvent">
  <Copy SourceFiles="$(TargetDir)$(TargetName).dll"
        DestinationFolder="..\..\App\bin\$(Configuration)\net10.0-windows\Plugins\"
        SkipUnchangedFiles="true" />
  <Copy SourceFiles="$(TargetDir)$(TargetName).dll"
        DestinationFolder="..\..\Service\bin\$(Configuration)\net10.0-windows\Plugins\"
        SkipUnchangedFiles="true" />
</Target>
```

Copia también las dependencias administradas o nativas de terceros a esa misma carpeta con un elemento `<Copy>` hermano si tu plugin las necesita en el momento de la carga.

## 3. Recursos de localización incrustados

Si el plugin implementa la interfaz [`ITranslationProvider`](./sdk/ui-extensions), se recomienda empaquetar los archivos JSON como **Recursos incrustados**:

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Translations\**\*.json" />
</ItemGroup>
```

Organiza los archivos como `Resources/Translations/{cultura}/{tipo}.json`, donde `{tipo}` es el `typeName` que pasas a `TranslationService.LoadEmbeddedTranslations(assembly, cultureKey, typeName)`. Todos los plugins de este repositorio usan el nombre fijo **`Plugin.json`** (`Resources/Translations/es-ES/Plugin.json`, `Resources/Translations/en-US/Plugin.json`, …) y pasan `"Plugin"`; `App.json` no es una convención de los plugins — existe solo en CoreExtensions, que además aporta las cadenas de la propia interfaz del anfitrión. Las carpetas de cultura siguen los siete idiomas de la aplicación; cuando la cultura solicitada no tiene carpeta, el gestor de traducciones recurre primero a la primera cultura admitida por el propio plugin, después a `en-US`, y solo entonces al marcador de posición `[key]`.

## 4. Definición de versión y metadatos

Especifica la versión y la descripción en el `.csproj`:

```xml
<PropertyGroup>
  <Version>1.2.0</Version>
  <AssemblyVersion>1.2.0.0</AssemblyVersion>
  <FileVersion>1.2.0.0</FileVersion>
  <Description>Plugin de extensión para fuentes de búsqueda y acciones contextuales.</Description>
</PropertyGroup>
```

Esta información se presentará de forma automática en la tarjeta de **Configuración → Plugins**.

## 5. Compilación de Release y artefactos por arquitectura

Antes de ejecutar `make.bat` desde la raíz del repositorio en Windows, instala el SDK de .NET y la [edición de 64 bits de Inno Setup 7](https://jrsoftware.org/isdl.php#v7) (el script comprueba que el compilador sea realmente 7.x). Llama a `:build_arch` dos veces: una con `ARCH=x64` (publicado sin RID, como siempre) y otra con `ARCH=arm64` (una publicación cruzada con `-r win-arm64 --self-contained false`), y genera cuatro archivos en `dist/`:

- `Lertaro-Setup.exe` y `Lertaro-Portable.zip` (x64).
- `Lertaro-Setup-arm64.exe` y `Lertaro-Portable-arm64.zip` (arm64).

Las dos arquitecturas se publican como artefactos separados y no como un instalador único porque el plugin BrowserData lleva una librería nativa, y su directorio de carga plano, sin `.deps.json`, solo puede alojar la copia nativa de una arquitectura (mira el comentario de cabecera del script). El instalador arm64 difiere del de x64 en `Installer/installer.iss` en `ArchitecturesAllowed` (`arm64` frente a `x64compatible`), `ArchitecturesInstallIn64BitMode`, `SetupArchitecture=x64` (solo en x64), el archivo del entorno de ejecución de .NET Desktop incluido y su URL de descarga, `PublishDir` y el nombre del archivo de salida; la carga útil de cada uno es nativa de su arquitectura. El flujo de publicación calcula las sumas de verificación y sube precisamente esos cuatro nombres, y `UpdateAssetSelector` empareja las instalaciones antiguas por nombre — cambiar el nombre de un artefacto dejaría sin actualización a los usuarios ya instalados. Mantén los nombres de los artefactos alineados con `make.bat`, `Installer/installer.iss` y la lista de recursos del flujo de publicación.
