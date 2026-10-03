# Primeros pasos

¡Te damos la bienvenida a Lertaro! Lertaro es un lanzador de búsqueda de archivos y una herramienta de productividad diseñada a medida para Windows. Esta guía te explica las opciones de instalación, la arquitectura principal, las tres modalidades de ventana y las operaciones de búsqueda esenciales.

## 1. Descarga e instalación

Puedes obtener la última versión en la página principal oficial. Cada versión publicada ofrece instalador y paquete portable para aplicaciones compiladas de forma nativa tanto para **x64** como para **ARM64**:

### Paquete de instalación (`Lertaro-Setup.exe`, recomendado)

- **Configuración automatizada**: El asistente detecta e instala el entorno de ejecución de escritorio de .NET necesario; al ejecutar Lertaro por primera vez, la propia aplicación registra el servicio de indexación en segundo plano y configura la entrada de inicio.
- **Actualizaciones in situ**: Admite comprobación automática de actualizaciones e instalación directa con un solo clic.

### Edición portátil (`Lertaro-Portable.zip`)

- **Descomprimir y usar**: Descomprime en cualquier carpeta y ejecútalo inmediatamente sin necesidad de instalación formal.
- **Dependencias de entorno**: Si tu sistema no cuenta con el entorno de ejecución de escritorio de .NET, ejecuta el script `install-dotnet-runtime.bat` incluido en el directorio descomprimido.
- **Aislamiento de datos**: La versión portátil guarda los datos globales del equipo en `Data\Machine` junto al ejecutable, y las configuraciones de usuario en `Data\Users\<SID hash>`. Si el directorio `Data` aún no existe, leerá `%ProgramData%\Lertaro` y `%LocalAppData%\Lertaro` por compatibilidad; una vez creado, priorizará los datos locales como un entorno completamente autónomo.
- **Carpeta protegida al instalar el servicio**: Al instalar el servicio en segundo plano (aceptando una solicitud de UAC), la carpeta portátil queda protegida. Solo los administradores pueden modificar los archivos de la aplicación, `Data\Machine` solo lo escribe el servicio y la carpeta `Data\Users\<hash del SID>` de cada usuario es privada para ese usuario. Por eso, actualizar o eliminar una copia portátil requiere permisos de administrador; `portable-cleanup.bat` los solicita por sí mismo.
- **Desinstalación limpia**: Antes de eliminar la carpeta portátil, ejecuta el script `portable-cleanup.bat`. Este detiene y desinstala el servicio en segundo plano, y elimina los registros URI `lertaro://` y las entradas de inicio del usuario actual.

> [!TIP]
> Si utilizas un dispositivo Windows on ARM (como Surface Pro X o portátiles con Snapdragon), descarga la versión nativa `Lertaro-Setup-arm64.exe` o `Lertaro-Portable-arm64.zip` para obtener el mejor rendimiento y eficiencia energética.

## 2. Descripción de la arquitectura

Al ejecutar Lertaro por primera vez, se instala e inicia un servicio de Windows en segundo plano (nombre de servicio `LertaroService`, ejecutable `Lertaro.Service.exe`). Comprender este diseño te ayudará a aprovechar al máximo la herramienta:

- **Aplicación en primer plano (UI e interacción)**: Renderiza las ventanas de búsqueda, paneles flotantes, menús de acción, interceptores de teclado y previsualizaciones instantáneas. Mantiene un bajo consumo de memoria y respuesta instantánea.
- **Servicio en segundo plano (Indexación y datos)**: Se ejecuta con privilegios de servicio en segundo plano, monitorizando los diarios de cambios USN de NTFS / ReFS, escuchando eventos en tiempo real de otros sistemas de archivos, gestionando recursos de red y manteniendo un árbol de índices en memoria.
- **Ventajas arquitectónicas**: Reiniciar o actualizar la interfaz nunca borra el índice ni fuerza un reescaneo del disco. Las tareas pesadas de indexación nunca ralentizan tu escritura. Puedes comprobar el estado del servicio en cualquier momento en [**Configuración → Estado del servicio**](./settings/service-status).

## 3. Tres modalidades de ventana

Lertaro ofrece tres modalidades de ventana diseñadas a medida para distintos flujos de trabajo:

| Modalidad de ventana | Activador predeterminado | Características clave y diseño | Escenario ideal |
| :--- | :--- | :--- | :--- |
| **Ventana rápida (Quick Window)** | Doble pulsación de `Ctrl` (personalizable) | Barra flotante centrada en pantalla, con búsqueda al escribir, atajos numéricos y navegación completa con teclado | Inicio frecuente de apps, búsqueda rápida de archivos, cálculos y traducciones |
| **Ventana principal (Full Window)** | Barra de tareas/Menú Inicio, o `Ctrl+F` | Ventana completa con vista de tabla para grandes volúmenes de resultados, filtros laterales, ordenación y Analizador de espacio | Exploración de archivos, gestión masiva, limpieza de disco y filtrado avanzado |
| **Ventana incrustada (Inline Window)** | Escribe una letra o un dígito dentro de un diálogo de archivos o de un explorador (o acóplala en el Explorador) | Integrada directamente en los diálogos de Windows y en los exploradores de archivos compatibles. La tarjeta nunca roba el teclado: sigue escribiendo en el campo propio del diálogo y haz una doble pulsación de `Ctrl` (tu atajo de invocación configurado) para mover el cursor a la tarjeta | Localización rápida de destinos al "Abrir" o "Guardar como" en software externo |

Las tres modalidades comparten exactamente el mismo motor de búsqueda, combinaciones de teclas, reglas de filtrado y menús de acción.

## 4. Primera búsqueda y navegación básica

### Escribe para buscar

Abre la ventana de búsqueda y empieza a escribir. Los resultados aparecen en tiempo real con latencia de milisegundos. La búsqueda utiliza coincidencia difusa por defecto: los caracteres no necesitan ser consecutivos. Para consultar operadores y modificadores avanzados, consulta [**Sintaxis de búsqueda**](./search-syntax).

### Navegación y apertura de resultados

- **Mover selección**: Usa las flechas `↑` / `↓` (o las teclas de navegación configuradas `Ctrl+P` / `Ctrl+N`) para desplazarte por la lista.
- **Abrir directamente**: Pulsa `Enter` para abrir el archivo o iniciar la aplicación seleccionada.
- **Mostrar en Explorador**: Pulsa `Ctrl+Enter` para ubicar y seleccionar el elemento en el Explorador de archivos de Windows.
- **Ejecutar como administrador**: Pulsa `Ctrl+Shift+Enter` para ejecutar la aplicación con privilegios elevados.
- **Salto numérico directo**: En la Ventana rápida, pulsa `Ctrl` + `1`–`9` para abrir directamente cualquiera de los primeros 9 resultados.

### Menú de acciones y operaciones avanzadas

Pulsa `Ctrl+O` o la flecha derecha `→` en el elemento resaltado para desplegar el **Menú de acciones**, que permite copiar rutas, ver propiedades, manipular archivos o invocar extensiones de plugins. Esas dos teclas pertenecen a la Ventana rápida (y a la tarjeta incrustada fuera de los diálogos de archivos); en la Ventana principal usa en su lugar el clic derecho o la tecla `Apps`. Consulta [**Acciones y vista previa**](./actions-and-preview) y [**Atajos de teclado**](./hotkeys) para más detalles.
