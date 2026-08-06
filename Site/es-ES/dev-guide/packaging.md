# Empaquetado y despliegue

## Cómo se detectan los plugins

La App carga todas las `.dll` que encuentra en su propia carpeta `Plugins/` (junto a
`Lertaro.App.exe`) al iniciarse, buscando tipos que implementen `IPlugin`. No existe un archivo de manifiesto
independiente — el propio ensamblado, junto con las interfaces del SDK que implementen sus tipos, constituye el
contrato completo.

## Distribuir tus propias dependencias

Si tu plugin necesita DLL de dependencias propias, administradas o nativas (un controlador de base de datos, una
biblioteca de interoperabilidad nativa, ...), colócalas en su propio subdirectorio dentro de la carpeta `Plugins/` de la
App — por ejemplo, `Plugins/YourPlugin/YourPlugin.dll` junto con sus dependencias justo al lado — en lugar de
ponerlas directamente en `Plugins/`. El cargador escanea `Plugins/` de forma recursiva, y la propia búsqueda de
dependencias en el mismo directorio de `Assembly.LoadFrom` resuelve entonces tus dependencias automáticamente, sin
propagarlas al directorio de carga de ningún otro plugin.

Un archivo que no sea de .NET que el escáner encuentre por el camino (una DLL nativa como `e_sqlite3.dll`) es
algo esperado y se registra en nivel `Debug`, no `Error` — solo un ensamblado administrado real que realmente falle al
cargarse se registra en nivel `Error`.

Consulta el `.csproj` del plugin `BrowserData` para ver un ejemplo completo y real: incluye
`Microsoft.Data.Sqlite` y sus dependencias nativas `SQLitePCLRaw`/`e_sqlite3.dll` de esta manera, con
targets post-build/post-publish que las anidan en su propia subcarpeta independientemente de qué mecanismo de compilación
las haya generado.

## Automatizar la copia durante el desarrollo

Los plugins distribuidos con el propio Lertaro (`CoreExtensions`, `PinyinAlias`) automatizan el despliegue
con un target post-build en su `.csproj`, copiando la DLL recién compilada directamente en la carpeta `Plugins/` de
salida de la propia App, de modo que una recompilación se detecta de inmediato en el siguiente arranque:

```xml
<Target Name="PostBuild" AfterTargets="PostBuildEvent">
  <Copy SourceFiles="$(TargetDir)$(TargetName).dll"
        DestinationFolder="..\..\App\bin\$(Configuration)\net10.0-windows\Plugins\"
        SkipUnchangedFiles="true" />
</Target>
```

Adapta la ruta de destino a donde realmente residan la salida de tu propia compilación y la instalación de la
App de Lertaro.

## Traducciones incrustadas

Si tu plugin implementa `ITranslationProvider` (ver
[Extensiones de interfaz y vista previa](./sdk/ui-extensions)), distribuye sus archivos JSON de traducción como recursos
incrustados en lugar de archivos sueltos, para que viajen junto con la DLL:

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Translations\**\*.json" />
</ItemGroup>
```

`TranslationService.LoadEmbeddedTranslations` (ver [Servicios del host](./sdk/services)) los recupera
del ensamblado en tiempo de ejecución por nombre de cultura.

## Versionado

Asigna un `<Version>` al `.csproj` de tu plugin; se muestra a los usuarios en su tarjeta bajo
**Configuración → Plugins**, junto con la versión de `PluginSdk` contra la que se compiló tu plugin — útil
para confirmar la compatibilidad cuando cambia la superficie del SDK.
