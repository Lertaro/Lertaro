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
        ├── MyCustomPlugin.dll           (Ensamblado principal)
        ├── ThirdParty.Managed.dll       (Dependencia administrada)
        └── x64/
            └── NativeLibrary.dll        (DLL nativa en C/C++)
```

- **Resolución automática de dependencias**: Al cargar la DLL principal mediante `Assembly.LoadFrom`, el entorno de .NET resuelve automáticamente las dependencias adyacentes sin interferir con otros plugins.
- **Tolerancia a archivos nativos**: Si se encuentran librerías nativas (p. ej. `e_sqlite3.dll`), el cargador las registra como `Debug` y continúa con seguridad sin registrar errores falsos.

## 2. Configuración de copia automática PostBuild

Añade un destino `PostBuild` en el archivo `.csproj` del plugin para desplegar los archivos automáticamente tras cada compilación exitosa:

```xml
<Target Name="PostBuild" AfterTargets="PostBuildEvent">
  <ItemGroup>
    <PluginOutputFiles Include="$(TargetDir)**\*.*" />
  </ItemGroup>
  <Copy SourceFiles="@(PluginOutputFiles)"
        DestinationFolder="..\..\App\bin\$(Configuration)\net10.0-windows\Plugins\$(TargetName)\%(RecursiveDir)"
        SkipUnchangedFiles="true" />
</Target>
```

## 3. Recursos de localización incrustados

Si el plugin implementa la interfaz [`ITranslationProvider`](./sdk/ui-extensions#itranslationprovider), se recomienda empaquetar los archivos JSON como **Recursos incrustados**:

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Translations\**\*.json" />
</ItemGroup>
```

Se sugiere estructurar los archivos según el esquema `Resources/Translations/{CultureName}/{TypeName}.json` (p. ej. `es-ES/MyCustomPlugin.json`, `en-US/MyCustomPlugin.json`). `TranslationService.LoadEmbeddedTranslations` los cargará dinámicamente según el idioma activo.

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

Antes de ejecutar `make.bat` desde la raíz del repositorio en Windows, instala el SDK de .NET y la [edición de 64 bits de Inno Setup 7](https://jrsoftware.org/isdl.php#v7). El script crea salidas de publicación independientes para x64 y `win-arm64`, y genera estos archivos en `dist/`:

- x64: `Lertaro-Setup.exe` y `Lertaro-Portable.zip`.
- ARM64: `Lertaro-Setup-arm64.exe` y `Lertaro-Portable-arm64.zip`.

El ejecutable de la aplicación incluido en los artefactos ARM64 es nativo ARM64. El instalador ARM64 utiliza un bootstrapper de Inno Setup compatible, mientras que el instalador x64 utiliza el shell de 64 bits de Inno Setup 7. Mantén alineados la carga útil de cada arquitectura y los sufijos de los archivos en `make.bat`, `Installer/installer.iss` y el flujo de publicación.
