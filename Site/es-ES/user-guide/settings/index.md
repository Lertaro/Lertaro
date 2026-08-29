# Referencia de configuración

Lertaro ofrece un conjunto exhaustivo y granular de opciones de personalización. Tanto si deseas ajustar las dimensiones en píxeles de la barra de búsqueda como personalizar atajos globales, modificar la frecuencia de indexación o gestionar plugins y espacios de trabajo, todo se configura desde el Centro de Configuración.

## 1. Características de la ventana e interacción

- **Ventana redimensionable y maximizable**: La ventana de Configuración permite ajustar libremente sus bordes, maximizar con doble clic en la barra de título y recuerda sus dimensiones automáticamente.
- **Búsqueda global en Configuración**: En la esquina superior derecha de la barra de título se incluye un buscador dedicado. Utiliza el motor de coincidencia difusa fzf de Lertaro para buscar entre todas las secciones, incluidos los ajustes de plugins y acciones. Al pulsar Intro, salta directamente a la opción y la resalta con un borde parpadeante.
- **Barras de pestañas desplazables**: En las secciones con varias subpestañas (como General, Atajos, Indexación), aparecen flechas de navegación a ambos lados para garantizar que todas las pestañas permanezcan visibles en cualquier idioma.

## 2. Resumen de secciones de configuración

La barra lateral izquierda contiene las siguientes diez secciones principales:

| Sección | Contenido principal |
| :--- | :--- |
| **[Estado del servicio](./service-status)** | Estado del servicio de Windows en segundo plano, diagnóstico y visor de registros en vivo para Service, App y Hook. |
| **[Gestión de indexación](./index-drives)** | Unidades locales (NTFS / ReFS / FAT32), unidades de red (SMB / NAS), distribuciones WSL, carpetas personalizadas y reglas de exclusión. |
| **[Configuración general](./general)** | Inicio automático, aceleración por hardware, servicio compatible con IPC, dimensiones de la barra de búsqueda y opciones de vista previa. |
| **[Atajos de teclado](./hotkeys-page)** | Atajos de búsqueda global, teclas de navegación, accesos de plugins, lista negra de procesos y omisión en pantalla completa. |
| **[Plugins](./plugins)** | Lista de plugins instalados, activación por componentes, formularios de configuración y ecosistema de Flow Launcher. |
| **[LocalSend](./localsend)** | Configuración del protocolo de transferencia inalámbrica compatible con LocalSend (nombre de dispositivo, puerto, PIN y autoguardado). |
| **[Favoritos](./favorites)** | Accesos directos con estrella a carpetas, archivos y URLs con ordenación por arrastre y búsqueda rápida por alias. |
| **[Historial](./history)** | Gestión de resultados abiertos y términos de búsqueda consultados, reapertura inteligente y limpieza. |
| **[Inicio rápido](./quick-launch)** | Gestiona accesos directos manuales y elige las fuentes de datos dinámicas que aparecen cuando la ventana de búsqueda rápida está vacía. |
| **[Panel rápido](./quick-panel)** | Espacios de trabajo flotantes acoplados, agregación de carpetas de múltiples orígenes, recepción de archivos y pestañas de plugins. |
| **[Apariencia y temas](./appearance)** | Selector de modo Claro / Oscuro / Seguir al sistema y tarjetas con vista previa en miniatura de temas integrados y seleccionados. |
| **[Acerca de y actualizaciones](./about)** | Versiones de componentes, rutas a directorios de datos de usuario y equipo, rotación de copias de seguridad y actualización silenciosa. |

Los siguientes capítulos describen en detalle cada opción, rango de parámetros y comportamiento predeterminado de cada sección.
