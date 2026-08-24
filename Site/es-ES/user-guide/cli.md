# Búsqueda por línea de comandos (lff)

Lertaro incluye una herramienta complementaria de consola ligera y eficiente denominada **`lff`** (Lertaro Fuzzy Finder), un buscador difuso interactivo diseñado para usuarios avanzados de terminal y scripts. Se comunica mediante canalizaciones con nombre locales con Lertaro App para reutilizar el árbol de índices en memoria sin reescanear las unidades.

## 1. Por qué elegir lff

- **Índice compartido, latencia de E/S cero**: A diferencia de `fzf` o `find` que recorren los sectores del disco desde cero en cada ejecución, `lff` consulta el índice en memoria de millones de elementos de Lertaro en submilisegundos.
- **Sintaxis de búsqueda idéntica**: Hereda la coincidencia difusa con saltos, los alias de pinyin y los operadores de modificación de Lertaro (ver [**Sintaxis de búsqueda**](./search-syntax)).
- **Cierre seguro inmediato**: Detecta automáticamente si Lertaro App está en ejecución. Si no es así, muestra un mensaje de error claro en `stderr` y sale de inmediato sin bloquear la terminal.

## 2. Instalación y configuración de PATH

`lff.exe` se distribuye incluido en el paquete de Lertaro:

- **Instalador**: Marca **Añadir herramienta de búsqueda lff a PATH** en el asistente para ejecutar `lff` desde cualquier terminal.
- **Edición portátil**: Añade manualmente la ruta de la carpeta descomprimida a la variable de entorno `PATH` del usuario o del sistema.

> [!NOTE]
> Tras actualizar la variable PATH, abre una nueva ventana de terminal para aplicar los cambios; las sesiones abiertas no reflejarán la modificación automáticamente.

## 3. Interfaz interactiva y combinaciones de teclas

Ejecuta `lff` en cualquier terminal para abrir la interfaz interactiva a pantalla completa:

```bash
lff
```

### Tabla de combinaciones de teclas

| Tecla | Descripción |
| :--- | :--- |
| **Escribir caracteres** | Filtra los resultados en tiempo real con coincidencia difusa. |
| `↑` / `↓` | Mueve el resaltado hacia arriba / abajo. |
| `Page Up` / `Page Down` | Desplaza la vista por páginas completas. |
| `←` / `→` | Mueve el cursor horizontalmente dentro del campo de búsqueda. |
| `Tab` | Alterna la selección/marca en la fila resaltada (los elementos marcados muestran un `*`). |
| `Enter` | Envía todas las rutas marcadas (o la ruta resaltada si no hay marcas) a `stdout` y sale. |
| `Esc` o `Ctrl+C` | Sale limpiamente sin emitir ninguna salida. |

## 4. Búsquedas predefinidas y entrada por tubería

Puedes proporcionar un término de búsqueda inicial mediante argumentos de línea de comandos o por la entrada estándar:

```bash
# Mediante argumento
lff report

# Mediante tubería de entrada estándar
echo report | lff
```

Ambos métodos abren la interfaz interactiva con `report` precargado como filtro inicial, permitiéndote ajustar la búsqueda o pulsar `Enter` directamente.

## 5. Selección múltiple y salida por lotes

Pulsa `Tab` para marcar elementos. **Las selecciones marcadas se mantienen incluso si cambias el término de búsqueda**.

Puedes buscar `doc` para marcar varios documentos de Word, borrar la búsqueda, buscar `pdf` para marcar informes y pulsar `Enter`: `lff` enviará todas las rutas marcadas a la salida estándar, una por línea.

## 6. Integración en scripts de consola

La interfaz de `lff` se dibuja directamente en el búfer de la consola sin interferir con el flujo de salida estándar. Solo las cadenas de rutas confirmadas se escriben en `stdout`, facilitando su combinación con otras herramientas:

### Flujos de trabajo en PowerShell

```powershell
# Abrir el archivo seleccionado en VS Code
code (lff)

# Asignar la carpeta seleccionada a una variable y navegar a ella
$target = lff; cd $target

# Enviar los resultados como objetos FileInfo por la tubería
lff | Get-Item | Select-Object Name, Length, LastWriteTime
```

### Flujos de trabajo en CMD / Batch

```cmd
:: Procesar las rutas seleccionadas línea a línea en un bucle for
for /f "delims=" %i in ('lff') do code "%i"
```

## 7. Límites y decisiones de diseño

- **Requiere la aplicación en primer plano**: `lff` depende de la instancia activa de Lertaro App; no realiza indexación autónoma.
- **Sin vista previa gráfica**: Optimizado exclusivamente para operaciones rápidas en consola. Para vistas previas interactivas y multimedia, utiliza la interfaz gráfica en [**Acciones y vista previa**](./actions-and-preview).
