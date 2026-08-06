# Primeros pasos

## Crear la estructura de un proyecto de plugin

Un plugin es una biblioteca de clases .NET normal que apunta al mismo framework de destino que la aplicación host
(`net10.0-windows`), y que referencia `PluginSdk`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>
    <AssemblyName>YourCompany.Plugins.YourPlugin</AssemblyName>
    <Version>1.0.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <!-- Reference Lertaro.PluginSdk.dll from your Lertaro install directory, or PluginSdk.csproj
         directly if you're building inside the Lertaro repo itself. -->
    <ProjectReference Include="..\..\PluginSdk\PluginSdk.csproj" />
  </ItemGroup>
</Project>
```

`UseWPF` solo es necesario si tu plugin renderiza alguna interfaz WPF propia (una vista previa personalizada, un
diccionario de recursos de tema, etc.) — un plugin que sea pura lógica de proveedor de búsqueda no lo necesita.

## Implementar `IPlugin`

Todo plugin tiene exactamente un punto de entrada que implementa `IPlugin`:

```csharp
public class YourPlugin : IPlugin
{
    public string Name => "Your Plugin";
}
```

A partir de ahí, implementa las interfaces adicionales que tu plugin realmente necesite — consulta la
[Referencia del SDK de Plugins](./sdk/core-search-actions) para ver la lista completa. La mayoría de los plugins reales implementan
`IPlugin` más una o dos más (`CoreExtensionsPlugin` implementa `IPlugin`, `IActionProvider` e
`IConfigurable`; ver [Plugins de ejemplo](./examples)).

## Cargarlo

Compila tu plugin y copia la DLL resultante en la carpeta `Plugins/` de la App de Lertaro, junto a
`Lertaro.App.exe` — la App escanea esa carpeta al iniciarse y carga todos los ensamblados de plugin que
encuentra. Consulta [Empaquetado y despliegue](./packaging) para ver cómo los plugins distribuidos automatizan este paso
como parte de su propia compilación.

## Depuración

Usa `Logger.Log(message, level)` (de `PluginSdk`) en todo tu plugin — su salida aparece en la
pestaña de registro de la **App**, en **Configuración → Estado del Servicio**, filtrable por nivel y buscable por
palabra clave, exactamente igual que los propios registros de la aplicación host.
