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
- **Las dependencias nativas van junto a la DLL, no bajo `runtimes\<rid>\native`**: los proyectos de plugin incluidos en el repositorio fijan `<GenerateDependencyFile>false</GenerateDependencyFile>`, así que no se genera ningún `.deps.json` y el entorno no dispone de un grafo de dependencias desde el que resolver una subcarpeta de RID. Por lo tanto, una librería nativa tiene que poder encontrarse en el directorio base de la aplicación: cópiala en plano. También es la razón de que las dos arquitecturas se publiquen como artefactos separados y no como un instalador único: una sola copia nativa, la de una arquitectura, puede ocupar ese directorio de carga plano.
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

Organiza los archivos como `Resources/Translations/{cultura}/{tipo}.json`, donde `{tipo}` es el `typeName` que pasas a `TranslationService.LoadEmbeddedTranslations(assembly, cultureKey, typeName)`. Todos los plugins de este repositorio usan el nombre fijo **`Plugin.json`** (`Resources/Translations/es-ES/Plugin.json`, `Resources/Translations/en-US/Plugin.json`, …) y pasan `"Plugin"`; `App.json` no es una convención de los plugins — existe solo en CoreExtensions, que además aporta las cadenas de la propia interfaz del anfitrión. Las carpetas de cultura siguen los siete idiomas de la aplicación, y una cultura sin carpeta simplemente recurre al texto por defecto del llamante.

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

Antes de ejecutar `make.bat` desde la raíz del repositorio en Windows, instala el SDK de .NET y la [edición de 64 bits de Inno Setup 7](https://jrsoftware.org/isdl.php#v7). El script, tal y como está ahora, invoca su rutina de compilación **una sola vez, para x64**, y genera:

- `Lertaro-Setup.exe` y `Lertaro-Portable.zip` en `dist/`.

Su comentario de cabecera describe dos arquitecturas («x64 se publica sin RID exactamente como siempre; arm64 es una publicación cruzada») y su pancarta final imprime las rutas de arm64, pero ninguna segunda llamada a `:build_arch` cambia `ARCH=x64`→`arm64`, así que una ejecución local no crea `Lertaro-Setup-arm64.exe` ni `Lertaro-Portable-arm64.zip` — aunque el flujo de publicación calcula las sumas de verificación y sube precisamente esos nombres. Tenlo presente antes de fiarte de la pancarta. El instalador arm64 solo difiere del de x64 en `ArchitecturesAllowed` (`arm64` frente a `x64compatible`) más `SetupArchitecture=x64` en `Installer/installer.iss`; la carga útil de cada uno es nativa de su arquitectura. Mantén los nombres de los artefactos alineados con `make.bat`, `Installer/installer.iss` y la lista de recursos del flujo de publicación.
