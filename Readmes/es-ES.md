<p align="center">
  <img src="../App/logo.png" alt="Lertaro logo" width="120">
</p>

# ⚡ Lertaro

[English](../README.md) | [简体中文](zh-CN.md) | [繁體中文（香港）](zh-HK.md) | [繁體中文（台灣）](zh-TW.md) | [日本語](ja-JP.md) | [한국어](ko-KR.md) | Español

> [!CAUTION]
> **Aviso de seguridad: descarga Lertaro únicamente desde fuentes oficiales.** El repositorio `github.com/adelmagical742/Lertaro` y el sitio web `adelmagical742.github.io` suplantan a Lertaro y distribuyen descargas maliciosas. No descargues ni ejecutes ningún archivo procedente de esas direcciones. El único repositorio oficial es [Lertaro/Lertaro](https://github.com/Lertaro/Lertaro), el único sitio web oficial es [lertaro.github.io](https://lertaro.github.io/) y los binarios oficiales se publican exclusivamente mediante [GitHub Releases](https://github.com/Lertaro/Lertaro/releases). Considera estas fuentes fraudulentas como no confiables aunque cambien el nombre o el contenido de los archivos.

Lertaro es un launcher de búsqueda global y productividad para Windows ultraligero, de alto rendimiento y extensible, construido sobre **.NET 10 (WPF)**. Es una alternativa moderna y de código abierto a **Everything** y **Listary** — indexa las unidades locales leyendo directamente el **USN Journal** y la MFT de NTFS, para una búsqueda casi instantánea y de bajo consumo de recursos.

📖 **[Documentación completa, Manual de Usuario y Manual de Desarrollador](https://lertaro.github.io/es-ES/)**

## Características principales

- **Indexación en milisegundos** — lee el USN Journal/MFT de NTFS directamente en lugar de recorrer directorios; un servicio en segundo plano de bajo consumo mantiene el índice sincronizado en tiempo real.
- **Búsqueda difusa estilo FZF** — coincidencia difusa de múltiples palabras clave con operadores de prefijo/sufijo/exacto/exclusión, además de alias en pinyin para nombres de archivo en chino.
- **Tres formas de buscar** — una ventana emergente rápida, una ventana principal completa, y una barra de búsqueda en línea que se acopla directamente al Explorador de archivos o a los diálogos de archivo nativos.
- **Vista previa QuickLook**, un menú de acciones al estilo del menú contextual, y atajos de teclado, todos reasignables.
- **SDK de plugins abierto** — extiende proveedores de búsqueda, alias, acciones del menú contextual, columnas de resultados, vistas previas y temas.
- **Aislamiento de procesos** — un servicio de indexación a nivel SYSTEM se mantiene separado de la interfaz de usuario de la app a nivel de sesión.

Consulta el **[Manual de Usuario](https://lertaro.github.io/es-ES/user-guide/)** para la sintaxis de búsqueda, todos los atajos de teclado y todas las opciones de configuración; el **[Manual de Desarrollador](https://lertaro.github.io/es-ES/dev-guide/)** para la arquitectura y la referencia del SDK de plugins.

## Descarga

Obtén la última versión desde la [página principal](https://lertaro.github.io/es-ES/) o directamente:

- **Versión x64 (procesadores Intel / AMD)**
  - [Instalador (Lertaro-Setup.exe)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup.exe) — recomendado, soporta el servicio en segundo plano.
  - [Portable (Lertaro-Portable.zip)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable.zip) — sin instalación, descomprime y ejecuta.
- **Versión nativa ARM64 (dispositivos Snapdragon / Windows en ARM)**
  - [Instalador (Lertaro-Setup-arm64.exe)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup-arm64.exe) — recomendado para dispositivos ARM.
  - [Portable (Lertaro-Portable-arm64.zip)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable-arm64.zip) — ejecutable portable nativo para ARM.

## Compilar desde el código fuente

Requisitos: Windows 10/11, .NET 10 SDK, Visual Studio 2022 o JetBrains Rider, y [Inno Setup](https://jrsoftware.org/isinfo.php) si quieres compilar el instalador.

- `build_and_run.bat` — recompila App/Core/Service/plugins y relanza todo localmente.
- `make.bat` — genera compilaciones Release para x64 y ARM64 en el directorio `dist/`.

Consulta el **[Manual de Desarrollador](https://lertaro.github.io/es-ES/dev-guide/)** para la arquitectura completa y el SDK de plugins.

## 🎁 Apoyo y donaciones

Si Lertaro te ha sido útil, ¡gracias por considerar hacer una donación!

- **USDT (TRC20)**: `TNDh3husX1trDW2ZPm4ZZYdoCoCRCZQXn5`

## Licencia

MIT License.
