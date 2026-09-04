# Respuestas instantáneas y funciones con palabras clave

Más allá de la búsqueda de archivos locales, Lertaro incluye un potente conjunto de herramientas de cálculo instantáneo, utilidades del sistema y extensiones de plugins mediante palabras clave. Las respuestas aparecen de inmediato sin esperar a los resultados de búsqueda de archivos.

## 1. Respuestas instantáneas siempre activas

Estas funciones no requieren ningún prefijo de activación; se ejecutan automáticamente cuando la entrada coincide con sus patrones:

### Calculadora y conversión de base

Escribe cualquier expresión aritmética directamente en la barra de búsqueda. Los resultados aparecen en tiempo real. Pulsa `Enter` para copiar el resultado al portapapeles:

```text
12 * (4 + 3)
100 * (1 - 0.15)
```

Admite conversiones de base numérica habituales:

```text
255 to hex
0xFF to dec
101010 to bin
```

### Expansión e inspección de variables de entorno

- **Expandir variables**: Escribe `%NAME%` (p. ej. `%PATH%`, `%APPDATA%`) para consultar su valor. Las variables con múltiples rutas como `PATH` se dividen en listas legibles línea por línea.
- **Búsqueda difusa de variables**: Escribe `%` seguido de una palabra clave (p. ej. `%temp`) para buscar entre todas las variables de entorno del sistema y de usuario.

### Ejecución rápida de comandos

Ejecuta comandos directamente sin abrir una terminal previamente:

- `#<comando>`: Abre el símbolo del sistema y ejecuta el comando **con privilegios de administrador** (p. ej. `#sfc /scannow` o `#net start Lertaro.Service`).
- `$<comando>`: Abre el símbolo del sistema y ejecuta el comando con **permisos de usuario estándar** (p. ej. `$ping 1.1.1.1` o `$ipconfig /all`).

### Apertura directa de URLs

Escribe o pega cualquier dirección que comience por `http://` o `https://` y pulsa `Enter` para abrirla de inmediato en tu navegador predeterminado. Al introducir una dirección web válida sin protocolo, como `example.com`, la ventana de búsqueda rápida genera dos resultados instantáneos: `https://...` y `http://...`. Cada resultado usa la presentación de dos líneas con la indicación para abrirlo en el navegador; el texto de búsqueda no se modifica y una dirección que ya incluye un protocolo produce un solo resultado.

### Importar texto del portapapeles en la ventana de búsqueda rápida

Al mostrar la ventana de búsqueda rápida sin texto prellenado, importa y selecciona automáticamente el texto no vacío del portapapeles si es distinto del último valor importado por esa ventana. El mismo valor no se importa repetidamente y nunca se sobrescriben las consultas prellenadas desde una URI o al volver desde la ventana de búsqueda completa.

## 2. Extensiones activadas por palabra clave (Plugins integrados)

Escribe una breve **palabra clave activadora + espacio** seguida de tu consulta para invocar funciones específicas de plugins. Todas las palabras clave se pueden personalizar en [**Configuración → Plugins**](./settings/plugins).

| Palabra clave predeterminada | Nombre del plugin | Descripción y caso de uso | Ejemplo de uso |
| :--- | :--- | :--- | :--- |
| `ps` | **Gestor de procesos** | Busca procesos en ejecución por nombre, PID o título de ventana (con pinyin). Pulsa Enter para finalizar. | `ps chrome` o `ps 1234` |
| `win` | **Conmutador de ventanas** | Busca y cambia entre ventanas abiertas con capturas en miniatura en segundo plano. | `win code` o `win navegador` |
| `bb` | **Marcadores del navegador** | Busca marcadores de Chrome, Edge y Firefox. | `bb github` |
| `bh` | **Historial del navegador** | Busca el historial de navegación de Chrome, Edge y Firefox. | `bh github` |
| `set` | **Búsqueda en Configuración** | Búsqueda difusa en las opciones de Lertaro. Al seleccionar una, salta a la página correspondiente y la resalta. | `set atajo` o `set difuso` |
| `flow` | **Puente Flow Launcher** | Muestra los plugins cargados de Flow.Launcher y sus palabras clave, aprovechando su ecosistema. | `flow` |
| `cs` | **Búsqueda de contenido** | Busca en el texto de documentos locales indexados y muestra archivos coincidentes con fragmentos. | `cs plan del proyecto` |

## 3. Motores de búsqueda web

El plugin de Búsqueda web incluye accesos directos para los principales motores. Escribe el prefijo seguido de tu consulta para buscar en el navegador:

| Atajo | Motor de búsqueda | Ejemplo | Descripción |
| :--- | :--- | :--- | :--- |
| `bd` | Baidu | `bd aprendizaje profundo` | Buscar en Baidu |
| `g` | Google | `g lertaro github` | Buscar en Google |
| `bing` | Bing | `bing documentacion microsoft` | Buscar en Bing |
| `gh` | GitHub | `gh Lertaro` | Buscar directamente en repositorios de GitHub |
| `wiki` | Wikipedia | `wiki mecanica cuantica` | Consultar artículos de Wikipedia |
| `yt` | YouTube | `yt lofi hip hop` | Buscar vídeos en YouTube |

Puedes añadir, editar o eliminar motores y plantillas de URL en **Configuración → Plugins → Búsqueda web → Configurar**.

## 4. Traducción instantánea

Escribe el activador predeterminado `tr` seguido del texto para traducirlo automáticamente al idioma de interfaz seleccionado en Lertaro:

```text
tr Hello, how are you today?
```

- Los resultados aparecen de forma asíncrona mientras escribes; pulsa `Enter` para copiar la traducción.
- Personaliza la palabra clave en **Configuración → Plugins → Traductor → Configurar**.

## 5. Filtros de archivos

En **Configuración → Plugins → Filtros de archivos → Configurar**, puedes vincular una palabra clave activadora a carpetas concretas para limitar una búsqueda normal a ellas:

- **Carpetas de destino**: una ruta por línea (p. ej. `D:\Ingenieria\Planos`). Los resultados provienen del índice de archivos, así que las carpetas deben estar cubiertas por el índice (una unidad local habilitada, o añadidas en **Configuración → Índice → Carpetas**, que también acepta rutas de red); una carpeta sin cobertura de índice se omite con una advertencia en el registro.
- **Reglas de coincidencia**: patrones de nombre de archivo como `*.dwg;*.dxf` (las carpetas siempre se incluyen).
- **Palabra clave activadora**: tras vincular un prefijo (p. ej. `cad`), al escribir `cad plano_pieza` en la ventana de búsqueda rápida la búsqueda normal del índice (coincidencia difusa por nombre de archivo, alias pinyin incluidos) se limita a las carpetas vinculadas. Si solo escribes la palabra clave, se te pedirá seguir escribiendo.

## 6. Comandos personalizados

En **Configuración → Plugins → Comandos personalizados → Configurar**, convierte scripts complejos, herramientas de consola o aplicaciones en comandos concisos:

- **Marcadores de posición de parámetros**: Admite marcadores posicionales `%s1`, `%s2`... y captura completa de consulta `%s`.
- **Integración con Navegación rápida**: Marca "Mostrar en Navegación rápida" para fijar el comando en el menú de [**Navegación rápida**](./hotkeys#3-navegacion-rapida-activadores-de-raton), con rutas de submenú usando `/` (p. ej. `HerramientasDev/ReiniciarServicio`).
