# Atajos de teclado (Configuración)

La página de Atajos de teclado centraliza la gestión de atajos de invocación global, teclas de navegación interna, accesos rápidos de plugins y reglas de listas negras de procesos. Contiene tres pestañas: **Global**, **Acciones de plugins** y **Lista negra de procesos**.

## 1. Global

### Atajos globales

- **Mostrar/Ocultar búsqueda rápida**: Grabador de teclas dedicado. Admite **modo de doble pulsación** (por defecto doble `Ctrl`, configurable a doble `Alt` o `Shift`) y **combinaciones estándar** (p. ej. `Alt+Space`).
- **Abrir el panel principal de forma predeterminada**: Casilla (desactivada por defecto). Al activarla, el atajo global abre la Ventana principal en lugar de la Ventana rápida. La primera vez solo la lleva al primer plano y le da el foco; si está visible pero inactiva la vuelve a activar, y si ya está activa, al pulsar de nuevo vuelve a la Ventana rápida de forma predeterminada (o se cierra directamente cuando **Cerrar al repetir el atajo** está activado). No se mantiene automáticamente siempre encima.
- **Responder al enfocar aplicaciones a pantalla completa**: Casilla (desactivada por defecto). Permite responder a los atajos incluso con juegos o reproductores a pantalla completa; si está desactivada, los atajos se omiten para no interrumpir.
- **Salto rápido (Quick Jump)**: Por defecto `Ctrl+G`. En diálogos de archivos, salta a la carpeta navegada recientemente en exploradores compatibles.
- **Menú de Navegación rápida**: No tiene atajo predeterminado. Puedes asignar un atajo global opcional para abrir el menú en cascada. Desde el escritorio o una aplicación normal usa el contexto del escritorio; en el Explorador de archivos y los cuadros de diálogo nativos usa el contexto de la ventana actual. Los exploradores y los cuadros de diálogo siguen permitidos aunque la protección normal de primer plano suprima los atajos globales.

### Teclas de función y navegación

Controles independientes de grabación de teclas que aceptan combinaciones personalizadas:

- **Seleccionar elemento siguiente / anterior**: Por defecto `Ctrl+N` / `Ctrl+P` (equivalente a `↓` / `↑`).
- **Modificador de salto numérico**: Por defecto `Ctrl`, combinado con números `1`–`9`.
- **Abrir Menú de acciones**: Por defecto `Ctrl+O` (equivalente a `→`). Las dos teclas se aplican a la Ventana rápida y a la tarjeta incrustada fuera de los diálogos de archivos; la Ventana principal abre el mismo menú con el clic derecho o la tecla `Apps`.
- **Autocompletar desde selección**: Por defecto `Ctrl+Tab`.
- **Vista previa instantánea QuickLook**: Por defecto `Alt+P`.
- **Término anterior / siguiente en historial**: Por defecto `Alt+Up` / `Alt+Down`.
- **Eliminar término del historial**: Por defecto `Ctrl+Delete`.
- **Abrir Ventana principal**: Por defecto `Ctrl+F`.
- **Abrir ventana LocalSend**: Por defecto `Ctrl+S`.
- **Fijar ventana (Mantener visible)**: Por defecto `Ctrl+T`.
- **Mostrar/Ocultar Panel rápido**: Por defecto `Ctrl+F2`.

### Activadores de ratón para Navegación rápida

- **Doble clic izquierdo en zona vacía**: Casilla (**desactivada por defecto**). Abre el menú de Navegación rápida en el escritorio o en el Explorador.
- **Clic central en zona vacía**: Casilla (activada por defecto). Abre el menú en el escritorio, Explorador o cuadros de diálogo.

Solo el escritorio y el Explorador hacen caso de la forma de doble clic; los exploradores de terceros aceptan únicamente el clic central. Los dos activadores se ignoran mientras el puntero está sobre la propia tarjeta de Lertaro, y ambos siguen vivos para los exploradores y los diálogos incluso cuando una lista negra o una pantalla completa silenciaría los activadores globales.

### Grabar un atajo

Cada pulsación de tecla va al cuadro en lugar de a la aplicación que hay detrás, lo que convierte al propio grabador en un conjunto de gestos:

- **`Escape`** cancela la grabación y borra el valor. No existe un "descartar y restaurar": el cuadro queda vacío en el momento en que lo pulsas.
- **`✕`** vacía el cuadro; **`↺`** devuelve el valor de fábrica de esa fila. Solo se muestra uno de los dos a la vez: vaciar mientras hay una combinación asignada, restaurar cuando ya está vacío.
- **Un modificador aislado** (`Ctrl`, `Alt`, `Shift`) se acepta únicamente en las filas que significan algo por sí solas: el atajo de invocación, que pasa a ser de doble pulsación, y el modificador de salto numérico. En una fila de combinación normal, pulsar y soltar un modificador solo **vacía** la fila en lugar de guardar un valor que nunca podría activarse.
- **Las combinaciones reservadas por Windows** (`Win+E`, `Win+D`, …) se rechazan: el valor se fuerza a vacío, así que el cuadro simplemente se queda en blanco.
- **Sin detección de conflictos.** Dos filas pueden contener la misma combinación y nada te avisa; solo los atajos de los elementos favoritos informan de un duplicado o de una combinación rechazada por el sistema, y solo después de aplicar.

### Atajos de cada favorito

Cada favorito ([**Configuración → Favoritos**](./favorites)) puede llevar su propio atajo **global a nivel de sistema**, que funciona incluso cuando Lertaro no tiene ninguna ventana abierta y lleva el explorador de archivos en primer plano hasta esa carpeta. A diferencia de las filas del grabador de arriba, requiere un modificador, rechaza un modificador aislado y no puede usar `F12`.

## 2. Acciones de plugins

Muestra todos los atajos registrados por plugins (p. ej. Copiar ruta completa `Ctrl+Shift+C`, Copiar nombre `Shift+C`, Cortar `Ctrl+X`, Copiar `Ctrl+C`, Pegar `Ctrl+V`, Eliminar `Delete`, Eliminación permanente `Shift+Delete`).

- **Vista agrupada**: Organizado con claridad por plugin de origen.
- **Reasignación individual**: Cada acción cuenta con su propio control de grabación.

## 3. Lista negra de procesos

Configura aplicaciones en primer plano ante las cuales Lertaro silenciará todos los atajos y activadores de ratón.

- **Sin distinción de mayúsculas**: Acepta `game.exe` o `game`.
- **Añadir individualmente**: Introduce el nombre del proceso y pulsa **Añadir proceso**.
- **Gestión por lotes**: Pulsa **Generar texto** para exportar la lista a texto multilínea, o pega una lista y pulsa **Aplicar a la lista**.
- **Exención automática en cuadros de diálogo**: Aunque una app esté en la lista negra, sus diálogos de selección de archivos conservan la Búsqueda incrustada y la Navegación rápida.
