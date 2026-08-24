# Atajos de teclado y gestos

Lertaro adopta una filosofía de interacción centrada en el teclado, complementada con prácticos gestos de ratón y navegación rápida en cascada. Excepto las teclas fijas, todos los atajos globales y de la aplicación se pueden personalizar en [**Configuración → Atajos de teclado**](./settings/hotkeys-page).

## 1. Tabla resumen de atajos globales

| Acción | Atajo predeterminado | Descripción y detalles de interacción |
| :--- | :--- | :--- |
| **Mostrar/Ocultar Ventana rápida** | Doble pulsación de `Ctrl` | Configurable en modo doble pulsación o combinación estándar (p. ej. `Alt+Space`, `Win+Space`). |
| **Salto rápido (Quick Jump)** | `Ctrl+G` | En cuadros de diálogo, salta directamente a la carpeta consultada recientemente en exploradores o en el Panel rápido. |
| **Seleccionar elemento siguiente** | `Ctrl+N` o `↓` | Mueve el resaltado hacia abajo. En el Panel rápido se desplaza fluidamente entre grupos. |
| **Seleccionar elemento anterior** | `Ctrl+P` o `↑` | Mueve el resaltado hacia arriba. En el Panel rápido también se desplaza entre grupos. |
| **Saltar a resultados 1–9** | `Ctrl` + `1`–`9` | Modificador personalizable. Aparecen distintivos numéricos junto a los resultados para apertura instantánea. |
| **Abrir Menú de acciones** | `Ctrl+O` o `→` | Despliega el menú contextual de acciones (copiar ruta, propiedades, ejecutar como admin, operaciones de archivo, etc.). |
| **Autocompletar desde selección** | `Ctrl+Tab` | Rellena la barra de búsqueda con el nombre o ruta completa del elemento seleccionado para refinamiento. |
| **Vista previa instantánea QuickLook** | `Alt+P` | Abre/cierra el panel lateral de vista previa (imágenes, documentos, reproducción de audio/vídeo, árboles de carpetas). |
| **Término de búsqueda anterior** | `Alt+Up` | Retrocede por el historial reciente de búsquedas. |
| **Término de búsqueda siguiente** | `Alt+Down` | Avanza por el historial reciente de búsquedas. |
| **Eliminar término de historial** | `Ctrl+Delete` | Elimina la palabra clave mostrada actualmente del historial de búsqueda. |
| **Abrir Ventana principal** | `Ctrl+F` | Abre la ventana principal de gran tamaño manteniendo la búsqueda actual. |
| **Abrir ventana LocalSend** | `Ctrl+S` | Abre la ventana de transferencia inalámbrica LocalSend para enviar archivos o texto a otros dispositivos. |
| **Fijar ventana (Mantener visible)** | `Ctrl+T` | Bloquea la ventana abierta al perder el foco (ideal para pegar búsquedas de varias fuentes). |
| **Mostrar/Ocultar Panel rápido** | `Ctrl+F2` | Acopla el panel rápido junto a la ventana activa con archivos recientes, favoritos y espacios de trabajo. |

## 2. Icono de búsqueda y gestos de ratón

El pequeño logotipo en la barra de búsqueda no es solo estético: ofrece múltiples gestos rápidos:

### Gestos del icono en la Ventana rápida

- **Clic izquierdo**: Despliega el menú contextual principal (Mostrar ventana principal, Cambiar atajo, Configuración, Acerca de, Salida limpia, Salir). "Mostrar ventana principal" transfiere tu búsqueda activa.
- **Clic izquierdo y arrastrar**: Arrastra la barra de búsqueda para reubicarla. **Mantener `Ctrl` mientras arrastras** bloquea el movimiento al **eje vertical**, manteniendo la alineación horizontal intacta.
- **Clic derecho**: Restablece instantáneamente la Ventana rápida a su posición central predeterminada en pantalla sin alterar dimensiones.
- **Clic central**: Alterna el estado "Fijar ventana". El logotipo se ilumina mientras está fijada.

> [!NOTE]
> Las coordenadas recordadas para la Ventana rápida son **coordenadas relativas proporcionales** a ese monitor. Al invocarla en otra pantalla con distinta resolución o escala DPI, Lertaro recalcula la posición automáticamente dentro de los límites visibles.

### Iconos en la Ventana incrustada y Ventana principal

- **Ventana incrustada**: Al incrustarse en diálogos nativos (Abrir/Guardar/Examinar), hacer clic izquierdo en el icono abre el menú de [**Navegación rápida**](#3-navegacion-rapida-activadores-de-raton); desactivado en el Explorador ordinario.
- **Ventana principal**: Exclusivamente decorativo.

## 3. Navegación rápida (Activadores de ratón)

La Navegación rápida te permite acceder a directorios frecuentes y archivos recientes únicamente con clics de ratón, sin teclear.

### Entornos compatibles

- **Espacio vacío del Escritorio**: Clic central (o doble clic izquierdo opcional) para abrir el menú. Al hacer clic en una carpeta o archivo se abre directamente.
- **Explorador de archivos**: Clic central en zonas vacías del Explorador; al hacer clic en un elemento, la ventana navega directamente a esa carpeta.
- **Exploradores de terceros**: Clic central en la lista de archivos de Directory Opus, Total Commander, XYplorer, Files y One Commander (ver [**Exploradores de archivos compatibles**](./file-manager-support)).
- **Diálogos de archivo**: Clic central o clic en el icono incrustado dentro de diálogos Abrir/Guardar para saltar a la carpeta de destino sin confirmar accidentalmente.

### Estructura del menú en cascada

Impulsado por el plugin **Folder Cascader**:

1. **Carpetas abiertas actualmente**: Agrupa y desduplica carpetas activas de todos los exploradores abiertos.
2. **Favoritos e Historial**: Muestra elementos marcados con estrella e historial de visitas recientes.
3. **Categorías personalizadas**: Configura submenús anidados en **Configuración → Plugins → Folder Cascader** (p. ej. `Trabajo/ProyectoA`).
4. **Añadir carpeta rápida (Botón `+`)**: Cada encabezado de submenú cuenta con un botón `+` para guardar el directorio activo directamente en esa categoría.

## 4. Teclas fijas básicas (No configurables)

Para garantizar un comportamiento coherente y determinista, las siguientes teclas actúan igual en todas las configuraciones:

| Tecla | Contexto | Comportamiento estándar |
| :--- | :--- | :--- |
| `Enter` | Lista de resultados | Abre el elemento seleccionado (archivo, carpeta, app o acción). |
| `Ctrl+Enter` | Lista de resultados | Muestra y selecciona el elemento en el Explorador de archivos de Windows. |
| `Ctrl+Shift+Enter` | Lista de resultados | Ejecuta la aplicación seleccionada con privilegios de administrador. |
| `Escape` | Cualquier contexto | Borra el texto de búsqueda si lo hay; cierra la ventana o sale del menú si ya está vacía. |
| `Backspace` | Menú de acciones | Sale del menú de acciones hacia la lista de resultados cuando el filtro está vacío. |
| `←` / `→` Flechas | Menú de acciones | Flecha izquierda vuelve al menú superior; flecha derecha entra en submenús. |
| `Alt+Space` | Todas las ventanas | Bloqueado para evitar activar el menú del sistema en ventanas sin marco. |
| `Alt+F4` | Ventanas Principal / Config | Cierra la ventana normalmente; bloqueado en ventanas Rápida, Incrustada y Vista previa. |

## 5. Atajos de acciones de plugins y lista negra

### Atajos de acciones de plugins

Los plugins pueden registrar atajos propios (p. ej. `Ctrl+Shift+C` para copiar rutas, o gestión de archivos: Cortar `Ctrl+X`, Copiar `Ctrl+C`, Pegar `Ctrl+V`, Eliminar `Delete`, Eliminación permanente `Shift+Delete`). Puedes consultarlos y reasignarlos en **Configuración → Atajos de teclado → Acciones de plugins**.

### Lista negra de procesos y omisión en pantalla completa

- **Omisión automática en pantalla completa**: Cuando una aplicación se ejecuta en pantalla completa exclusiva (como juegos 3D o reproductores de vídeo), Lertaro omite automáticamente todos los atajos globales para no interferir.
- **Lista negra de procesos personalizada**: Añade ejecutables en [**Configuración → Atajos de teclado**](./settings/hotkeys-page#lista-negra-de-procesos) (p. ej. `game.exe`) para silenciar los atajos mientras ese proceso esté en primer plano.
