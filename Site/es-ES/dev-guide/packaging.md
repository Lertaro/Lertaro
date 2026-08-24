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
