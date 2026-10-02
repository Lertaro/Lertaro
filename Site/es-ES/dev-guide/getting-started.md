# Guía de inicio rápido

Este capítulo describe cómo crear un proyecto de plugin nativo en C# para Lertaro desde cero, implementar las interfaces principales y probarlo localmente.

## 1. Configuración del proyecto de plugin

Un plugin de Lertaro es un proyecto de biblioteca de clases estándar de .NET 10. Crea una nueva biblioteca de clases en C# y configura el archivo `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <!-- Activa UseWPF solo si tu plugin crea controles XAML/WPF personalizados -->
    <UseWPF>true</UseWPF>
    <!-- Prefijo OBLIGATORIO: el cargador omite cualquier DLL cuyo nombre de
         ensamblado no empiece por "Lertaro.Plugins." (sin distinguir mayúsculas) -->
    <AssemblyName>Lertaro.Plugins.MyCustomPlugin</AssemblyName>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <!-- Referencia el SDK desde la carpeta de instalación de la aplicación Lertaro.
         En este repositorio los plugins incluidos usan en su lugar un
         <ProjectReference> a PluginSdk/. -->
    <Reference Include="Lertaro.PluginSdk">
      <HintPath>C:\Program Files\Lertaro\Lertaro.PluginSdk.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

> [!WARNING]
> El prefijo `Lertaro.Plugins.` es un filtro estricto, no una convención: el escaneo recursivo de `Plugins\**\*.dll` lee el nombre del ensamblado y ni siquiera refleja una DLL que no empiece por él, así que un plugin llamado `YourCompany.MyPlugin.dll` no se carga y nadie te avisa. Mantén el prefijo en `AssemblyName` y deja `<Private>false</Private>` para que el SDK no se copie en tu salida.

> [!TIP]
> Los plugins de lógica pura (como fuentes de búsqueda, alias o utilidades CLI) no necesitan `<UseWPF>`.

## 2. Implementar el punto de entrada `IPlugin`

Cada ensamblado de plugin contiene al menos una clase pública que implementa `IPlugin`, y el cargador la instancia como punto de entrada principal del plugin:

```csharp
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.MyCustomPlugin;

public class MyCustomPlugin : IPlugin
{
    public string Name => "My Custom Plugin";
    public string Description => "Ejemplo básico que demuestra la integración con el SDK de Lertaro.";
}
```

La reflexión encuentra **todas** las implementaciones de `IPlugin` del DLL y crea una instancia de cada una, así que publicar dos registra dos plugins con dos tarjetas de configuración. Envía una sola salvo que de verdad quieras dos entradas.

A partir de aquí, puedes implementar interfaces adicionales en esta clase o en clases de componentes independientes. Por ejemplo, implementa `IInstantResultProvider` para cálculos dinámicos o `IConfigurable` para formularios de configuración.

## 3. Despliegue y carga

1. Compila el proyecto para generar `Lertaro.Plugins.MyCustomPlugin.dll`.
2. Coloca el archivo DLL compilado (junto con sus dependencias de terceros) bajo la carpeta `Plugins\` de la raíz de la aplicación Lertaro. La convención es una subcarpeta por plugin y el escaneo es recursivo (`Plugins\**\*.dll`), así que `Plugins\MyCustomPlugin\` funciona; la automatización de compilación del repositorio deja los DLL directamente en `Plugins\` y eso también funciona.
3. Inicia o reinicia Lertaro; el proceso App escanea la carpeta `Plugins\` y carga todo ensamblado cuyo nombre lleve el prefijo.
4. Abre **Configuración → Plugins** para comprobar el estado y las opciones del plugin.

> [!NOTE]
> Si tu plugin aporta un `IAliasProvider` o un `ITranslationProvider`, el **Servicio** en segundo plano necesita ese mismo DLL: esos dos tipos también se cargan allí para que las filas indexadas lleven los alias y las etiquetas correctos. Por eso los proyectos de plugin incluidos copian su salida tanto a `App\bin\...\Plugins\` como a `Service\bin\...\Plugins\`.

## 4. Depuración y registro de eventos

Utiliza `Logger` (atención: vive en el espacio de nombres raíz `Lertaro.PluginSdk`, no en `Lertaro.PluginSdk.Services`) para registrar eventos dentro del plugin:

```csharp
using Lertaro.PluginSdk;

Logger.Log("Plugin inicializado correctamente y servicios registrados.", LogLevel.Info);
```

- La salida aparece en tiempo real en **Configuración → Estado del servicio → Pestaña App**.
- Puedes filtrar por nivel de gravedad (Error / Advertencia / Información / Depuración) y buscar por palabras clave para agilizar la depuración.
