# Atajos de teclado (Configuración)

La página de Atajos de teclado centraliza la gestión de atajos de invocación global, teclas de navegación interna, accesos rápidos de plugins y reglas de listas negras de procesos. Contiene tres pestañas: **Global**, **Acciones de plugins** y **Lista negra de procesos**.

## 1. Global

### Atajos globales

- **Mostrar/Ocultar búsqueda rápida**: Grabador de teclas dedicado. Admite **modo de doble pulsación** (por defecto doble `Ctrl`, configurable a doble `Alt` o `Shift`) y **combinaciones estándar** (p. ej. `Alt+Space`, `Win+Space`).
- **Abrir el panel principal de forma predeterminada**: Casilla (desactivada por defecto). Al activarla, el atajo global abre la Ventana principal en lugar de la Ventana rápida. La primera vez solo la lleva al primer plano y le da el foco; si está visible pero inactiva la vuelve a activar, y si ya está activa la cierra al pulsar de nuevo. No se mantiene automáticamente siempre encima.
- **Responder al enfocar aplicaciones a pantalla completa**: Casilla (desactivada por defecto). Permite responder a los atajos incluso con juegos o reproductores a pantalla completa; si está desactivada, los atajos se omiten para no interrumpir.
- **Salto rápido (Quick Jump)**: Por defecto `Ctrl+G`. En diálogos de archivos, salta a la carpeta navegada recientemente en exploradores compatibles.

### Teclas de función y navegación

Controles independientes de grabación de teclas que aceptan combinaciones personalizadas:

- **Seleccionar elemento siguiente / anterior**: Por defecto `Ctrl+N` / `Ctrl+P` (equivalente a `↓` / `↑`).
- **Modificador de salto numérico**: Por defecto `Ctrl`, combinado con números `1`–`9`.
- **Abrir Menú de acciones**: Por defecto `Ctrl+O` (equivalente a `→`).
- **Autocompletar desde selección**: Por defecto `Ctrl+Tab`.
- **Vista previa instantánea QuickLook**: Por defecto `Alt+P`.
- **Término anterior / siguiente en historial**: Por defecto `Alt+Up` / `Alt+Down`.
- **Eliminar término del historial**: Por defecto `Ctrl+Delete`.
- **Abrir Ventana principal**: Por defecto `Ctrl+F`.
- **Abrir ventana LocalSend**: Por defecto `Ctrl+S`.
- **Fijar ventana (Mantener visible)**: Por defecto `Ctrl+T`.
- **Mostrar/Ocultar Panel rápido**: Por defecto `Ctrl+F2`.

### Activadores de ratón para Navegación rápida

- **Doble clic izquierdo en zona vacía**: Casilla (activada por defecto). Abre el menú de Navegación rápida en el escritorio o en el Explorador.
- **Clic central en zona vacía**: Casilla (activada por defecto). Abre el menú en el escritorio, Explorador o cuadros de diálogo.

## 2. Acciones de plugins

Muestra todos los atajos registrados por plugins (p. ej. Copiar ruta completa `Ctrl+Shift+C`, Cortar `Ctrl+X`, Copiar `Ctrl+C`, Pegar `Ctrl+V`, Eliminar `Delete`, Eliminación permanente `Shift+Delete`).

- **Vista agrupada**: Organizado con claridad por plugin de origen.
- **Reasignación individual**: Cada acción cuenta con su propio control de grabación.

## 3. Lista negra de procesos

Configura aplicaciones en primer plano ante las cuales Lertaro silenciará todos los atajos y activadores de ratón.

- **Sin distinción de mayúsculas**: Acepta `game.exe` o `game`.
- **Añadir individualmente**: Introduce el nombre del proceso y pulsa **Añadir proceso**.
- **Gestión por lotes**: Pulsa **Generar texto** para exportar la lista a texto multilínea, o pega una lista y pulsa **Aplicar a la lista**.
- **Exención automática en cuadros de diálogo**: Aunque una app esté en la lista negra, sus diálogos de selección de archivos conservan la Búsqueda incrustada y la Navegación rápida.
