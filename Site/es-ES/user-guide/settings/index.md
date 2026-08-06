# Referencia de configuración

La ventana de configuración se redimensiona y se maximiza como cualquier otra: arrastra sus bordes, usa el botón
de maximizar de la barra de título, o haz doble clic en la barra de título. Merece la pena sobre todo en la página
de Plugins, que pone una lista de plugins y los ajustes de ese plugin uno al lado del otro y aprovecha el ancho.

En la barra de título de la ventana de Configuración hay un cuadro de búsqueda. Coincide de forma difusa (la misma
coincidencia al estilo fzf que usa la ventana de búsqueda principal, con soporte de alias en pinyin), no solo
subcadenas simples, en todas las secciones — incluidas las entradas por plugin bajo Plugins, la subpestaña
Acciones de plugins de Atajos. Cada resultado muestra una
ruta de migas de pan (por ejemplo, "Índice > Unidades de red") para que ajustes con el mismo nombre en distintas
pestañas se puedan distinguir. Seleccionar un resultado (clic, o Arriba/Abajo para resaltar e Intro) cambia a la
sección y pestaña correctas, desplaza el control exacto hasta que sea visible, y hace parpadear brevemente un
borde de resaltado a su alrededor.

Varias secciones (General, Atajos, Índice, Historial, Panel Rápido, Estado del Servicio) se dividen además en
su propia fila de subpestañas en la parte superior de la página. Si las etiquetas de las pestañas no caben todas
— lo más habitual en inglés, ya que las etiquetas traducidas suelen ser más largas que sus originales en chino —
aparecen botones de flecha izquierda/derecha en los extremos de la fila para que el resto siga siendo alcanzable
desplazándose, en lugar de quedar simplemente cortado.

La ventana de Configuración tiene diez secciones en su barra lateral izquierda:

| Sección | Cubre |
|---|---|
| [Estado del Servicio](./service-status) | Instalación del servicio en segundo plano, y el visor de registros de App/Hook/Service. |
| [Índice](./index-drives) | Unidades locales, unidades de red, distribuciones WSL (una vez detectadas), índices de carpetas y reglas de exclusión. |
| [General](./general) | Comportamiento de inicio, actualizaciones, idioma, diseño de la barra de búsqueda y tamaño de la ventana de vista previa. |
| [Atajos](./hotkeys-page) | Atajos globales, atajos de acción por plugin y la lista negra de procesos. |
| [Plugins](./plugins) | Plugins instalados y los interruptores de habilitar/deshabilitar por componente. |
| [LocalSend](./localsend) | Configuración de transferencia LAN entre dispositivos para archivos, carpetas y texto plano. |
| [Favoritos](./favorites) | Accesos directos con nombre personalizado a carpetas, archivos y URL. |
| [Historial](./history) | Historial de búsqueda e historial de palabras clave de la ventana rápida. |
| [Panel Rápido](./quick-panel) | El panel flotante acoplado sobre la ventana en primer plano: sus espacios de trabajo, las fuentes de cada uno, las pestañas que aportan los plugins, y a qué aplicaciones pertenece cada espacio. |
| [Apariencia](./appearance) | Selector de tema (con una tarjeta de vista previa por tema) y el modo "seguir el claro/oscuro del sistema". Fijado encima de Acerca de. |
| [Acerca de](./about) | Información de versión y comprobación de actualizaciones. |

Cada página de abajo documenta todas las opciones de esa sección, en orden, con su valor predeterminado y
cualquier rango válido.
