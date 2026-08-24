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
    <AssemblyName>YourCompany.Plugins.MyCustomPlugin</AssemblyName>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <!-- Referencia a Lertaro.PluginSdk.dll desde la carpeta de instalación de Lertaro -->
    <Reference Include="Lertaro.PluginSdk">
      <HintPath>..\..\App\Lertaro.PluginSdk.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

> [!TIP]
> Los plugins de lógica pura (como fuentes de búsqueda, alias o utilidades CLI) no necesitan `<UseWPF>`. Establecer `<Private>false</Private>` en `PluginSdk.dll` evita copiar innecesariamente el SDK en la salida de compilación.

## 2. Implementar el punto de entrada `IPlugin`

Cada ensamblado de plugin debe contener exactamente una clase pública que implemente la interfaz `IPlugin` como punto de entrada principal:

```csharp
using Lertaro.PluginSdk;

namespace YourCompany.Plugins.MyCustomPlugin;

public class MyCustomPlugin : IPlugin
{
    public string Name => "My Custom Plugin";
    public string Description => "Ejemplo básico que demuestra la integración con el SDK de Lertaro.";
}
```

A partir de aquí, puedes implementar interfaces adicionales en esta clase o en clases de componentes independientes. Por ejemplo, implementa `IInstantResultProvider` para cálculos dinámicos o `IConfigurable` para formularios de configuración.

## 3. Despliegue y carga

1. Compila el proyecto para generar `YourCompany.Plugins.MyCustomPlugin.dll`.
2. Coloca el archivo DLL compilado (junto con sus dependencias de terceros) en una subcarpeta bajo `Plugins\MyCustomPlugin\` dentro de la raíz de la aplicación Lertaro.
3. Inicia o reinicia Lertaro; el proceso App escaneará la carpeta `Plugins/` y cargará el ensamblado automáticamente.
4. Abre **Configuración → Plugins** para comprobar el estado y las opciones del plugin.

## 4. Depuración y registro de eventos

Utiliza `PluginSdk.Services.Logger` para registrar eventos dentro del plugin:

```csharp
using Lertaro.PluginSdk.Services;

Logger.Log("Plugin inicializado correctamente y servicios registrados.", LogLevel.Info);
```

- La salida aparece en tiempo real en **Configuración → Estado del servicio → Pestaña App**.
- Puedes filtrar por nivel de gravedad (Error / Advertencia / Información / Depuración) y buscar por palabras clave para agilizar la depuración.
