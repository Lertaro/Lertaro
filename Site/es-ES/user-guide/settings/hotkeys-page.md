# Atajos (página de configuración)

Tres pestañas: **Global**, **Acciones de plugins** y **Lista negra de procesos**. Ver la página de [Atajos de
teclado](../hotkeys) para saber qué hace realmente cada atajo — esta página documenta la propia interfaz de
configuración.

## Global

Grupo **Atajos globales**:

- **Mostrar/Ocultar búsqueda rápida** — control grabador; acepta un modificador suelto (modo de doble pulsación,
  `Ctrl` por defecto) o una combinación completa. Junto a él, **Seguir respondiendo mientras una app a pantalla
  completa tiene el foco** — casilla, desactivada por defecto — excluye este atajo (y Cambio rápido, y la
  activación de la búsqueda en línea) de la exención automática de pantalla completa descrita en [Lista negra de
  procesos](#process-blacklist) más abajo.
- **Cambio rápido** — `Ctrl+G` por defecto.

Grupo **Teclas de función**:

- Elemento siguiente (`Ctrl+N`), Elemento anterior (`Ctrl+P`), modificador de Saltar a resultado (`Ctrl` por
  defecto, emparejado con los dígitos 1–9), Abrir menú de acciones (`Ctrl+O`), Completar desde selección
  (`Ctrl+Tab`), QuickLook (`Alt+P`), Historial de palabras clave anterior/siguiente (`Alt+Arriba` /
  `Alt+Abajo`), Eliminar entrada del historial de palabras clave (`Ctrl+Supr`), Abrir ventana completa
  (`Ctrl+F` por defecto — abre la ventana completa directamente, trasladando la consulta actual; el mismo efecto
  que hacer clic izquierdo en el propio logotipo de la Ventana rápida y elegir Mostrar ventana principal, sin ese
  clic adicional).
- Todo grabador de aquí acepta cualquier tecla o combinación que pulses — incluidas teclas como una `Tab` suelta —
  y ese atajo tiene prioridad sobre cualquier significado predeterminado fijo que esa tecla pudiera tener por otro
  lado.

Grupo **Navegación rápida**:

- **Doble clic izquierdo en espacio vacío** — casilla, activada por defecto.
- **Clic central en espacio vacío** — casilla, activada por defecto.

## Acciones de plugins

Una entrada por cada acción que haya registrado un plugin (por ejemplo, copiar ruta, ejecutar como administrador),
agrupadas por nombre de plugin, cada una con su propio grabador de atajo. Recurre al valor predeterminado sugerido
por el propio plugin hasta que lo cambies.

## Lista negra de procesos {#process-blacklist}

Añade nombres de ejecutables (por ejemplo, `juego.exe`) cuyo foco en primer plano deba suprimir por completo los
atajos globales de Lertaro, la interceptación de pulsaciones de teclado y los disparadores de ratón de doble
clic/clic central de navegación rápida. No distingue mayúsculas de minúsculas, el sufijo `.exe` es opcional. Admite
el mismo patrón de añadir-uno / edición-masiva que las reglas de exclusión bajo **Índice**: un cuadro de texto de
una sola entrada más **Añadir proceso**, una lista de entradas existentes, y un cuadro de texto masivo con
**Generar texto** / **Aplicar texto**.

Esta es la solución para los conflictos de atajos con juegos a pantalla completa u otras aplicaciones que capturan
la entrada de teclado en bruto — ver [Solución de problemas](../troubleshooting#el-atajo-global-no-responde).
Cualquier aplicación en primer plano que sea realmente de pantalla completa recibe automáticamente el mismo trato,
sin necesidad de ninguna entrada aquí — a menos que **Seguir respondiendo mientras una app a pantalla completa
tiene el foco** (en **Global**, junto a Mostrar/Ocultar búsqueda rápida) esté activado, lo cual excluye por
completo de esa exención. En cualquier caso, un diálogo de archivos activo siempre está exento, así que la
navegación rápida sigue funcionando ahí.
