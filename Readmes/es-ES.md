<p align="center">
  <img src="../App/logo.png" alt="Lertaro logo" width="120">
</p>

# ⚡ Lertaro

[English](../README.md) | [简体中文](zh-CN.md) | [繁體中文（香港）](zh-HK.md) | [繁體中文（台灣）](zh-TW.md) | [日本語](ja-JP.md) | [한국어](ko-KR.md) | Español

> [!CAUTION]
> **Aviso de seguridad: descarga Lertaro únicamente desde fuentes oficiales.** El repositorio `github.com/adelmagical742/Lertaro` y el sitio web `adelmagical742.github.io` suplantan a Lertaro y distribuyen descargas no autorizadas. No descargues ni ejecutes ningún archivo proveniente de ellos. El único repositorio oficial es [Lertaro/Lertaro](https://github.com/Lertaro/Lertaro), el único sitio web oficial es [lertaro.github.io](https://lertaro.github.io/), y los ejecutables oficiales se publican exclusivamente mediante [GitHub Releases](https://github.com/Lertaro/Lertaro/releases).

Lertaro es un buscador y lanzador de productividad global ultraligero, de alto rendimiento y extensible para Windows, construido sobre **.NET 10 (WPF)**. Es una alternativa moderna y de código abierto a **Listary** y **Everything**, que indexa unidades locales mediante el **USN Journal** y $MFT de NTFS para búsquedas instantáneas con un consumo mínimo de recursos.

📖 **[Documentación Completa, Manual de Usuario y Manual de Desarrollador](https://lertaro.github.io/es-ES/)**

## Características Principales

- ⚡ **Indexación de Bajo Nivel USN y MFT** —— Lee directamente el USN Change Journal y $MFT de NTFS/ReFS en lugar de recorrer directorios; un servicio ligero en segundo plano mantiene el índice sincronizado en tiempo real con soporte para FAT32/exFAT y carpetas compartidas de red.
- 🎯 **Búsqueda Difusa estilo fzf y Alias** —— Coincidencia por salto de caracteres con tokens de ruta, prefijos/sufijos y operadores de inclusión/exclusión, junto con transliteración de alias para nombres no ASCII.
- 📂 **Tres Modos de Búsqueda y Acople Profundo** —— Ventana emergente rápida, ventana principal completa y barra incrustada que se acopla a los diálogos Abrir/Guardar y gestores de archivos (Explorador de Windows, Total Commander, Directory Opus, OneCommander).
- 🎬 **Menú de Acciones y Vista Previa QuickLook** —— Pulsa `Ctrl+O` para abrir el menú de acciones con integración en el menú contextual del Shell, o pulsa `Alt+P` para previsualizaciones instantáneas con QuickLook.
- 📊 **Analizador de Espacio en Disco Treemap Instantáneo** —— Explora visualmente el espacio ocupado mediante mapas de árbol en tiempo real sin necesidad de reescanear discos.
- 🧩 **SDK de Plugins Abierto y Puente de Ecosistema** —— Contratos limpios en C# .NET 10 para proveedores de búsqueda, alias, acciones y columnas personalizadas, además de compatibilidad con plugins de la comunidad Flow Launcher.
- 🛡️ **Aislamiento de 3 Procesos y Privacidad Offline** —— El servicio SYSTEM (`Lertaro.Service`), la interfaz WPF (`Lertaro.App`) y el proceso auxiliar de gancho (`Lertaro.Service --hook`) están estrictamente aislados. 100% local y sin telemetría.

Consulta el **[Manual de Usuario](https://lertaro.github.io/es-ES/user-guide/)** para sintaxis de búsqueda y atajos; consulta el **[Manual de Desarrollador](https://lertaro.github.io/es-ES/dev-guide/)** para arquitectura y referencia del SDK.

## Descarga

Obtén la última versión en la [página oficial](https://lertaro.github.io/es-ES/) o directamente:

- **x64 (Intel / AMD)**
  - [Instalador (Lertaro-Setup.exe)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup.exe) —— Recomendado, incluye el servicio en segundo plano.
  - [Portable (Lertaro-Portable.zip)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable.zip) —— Sin instalación, descomprimir y ejecutar.
- **ARM64 (Nativo para Snapdragon / Windows on ARM)**
  - [Instalador (Lertaro-Setup-arm64.exe)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup-arm64.exe) —— Recomendado para dispositivos ARM.
  - [Portable (Lertaro-Portable-arm64.zip)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable-arm64.zip) —— Versión portable nativa para ARM.

## Compilación desde el Código Fuente

Requisitos: Windows 10/11, .NET 10 SDK, Visual Studio 2022 o JetBrains Rider, e [Inno Setup](https://jrsoftware.org/isinfo.php) si deseas generar el instalador.

- `build_and_run.bat` —— Recompila App/Core/Service/plugins y reinicia todo localmente para desarrollo.
- `make.bat` —— Genera los paquetes de Release para x64 y ARM64 en el directorio `dist/`.

Consulta el **[Manual de Desarrollador](https://lertaro.github.io/es-ES/dev-guide/)** para la arquitectura completa y el SDK de plugins.

## 🎁 Donaciones y Soporte

Si Lertaro te resulta útil, ¡agradecemos mucho tu apoyo al proyecto!

- **USDT (TRC20)**: `TNDh3husX1trDW2ZPm4ZZYdoCoCRCZQXn5`

## Licencia

Licencia MIT.
