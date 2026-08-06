# Solución de problemas

## El atajo global no responde

1. Comprueba **Configuración → Estado del Servicio** — si el servicio en segundo plano no está en ejecución, reinstalarlo o reiniciarlo desde esa página suele solucionar problemas relacionados con la indexación, pero el propio atajo de mostrar/ocultar lo gestiona el proceso de la App, no el servicio, así que esto es una comprobación secundaria.
2. Si la aplicación en primer plano se ejecuta elevada (como administrador) y Lertaro no, Windows impide que los procesos con menos privilegios le envíen entradas. El proceso de escucha de atajos en segundo plano de Lertaro se eleva automáticamente siempre que tu cuenta sea administradora — sin ningún ajuste que activar, sin ningún aviso de UAC. Si los atajos siguen sin llegar a una ventana elevada, comprueba **[Configuración → Estado del Servicio](./settings/service-status)** para confirmar que el servicio en segundo plano está en ejecución, ya que es lo que lanza el proceso de escucha elevado. Si tu cuenta no es administradora, no hay ninguna solución alternativa — Windows no permite que un proceso con menos privilegios envíe señales a uno con más privilegios.
3. Comprueba la **[Lista negra de procesos](./settings/hotkeys-page#process-blacklist)** — si el nombre del ejecutable de la aplicación en primer plano se añadió ahí (a propósito o por accidente), los atajos globales de Lertaro se dejan pasar deliberadamente sin intervenir mientras esa aplicación tenga el foco.
4. Si la aplicación en primer plano es realmente de pantalla completa (ocupa todo el monitor), Lertaro también deja pasar automáticamente sus atajos por defecto — así no compite con juegos a pantalla completa. Si tu combinación configurada no va a chocar con nada que use la propia aplicación a pantalla completa, activa **Seguir respondiendo mientras una app a pantalla completa tiene el foco**, junto a **Configuración → Atajos → Mostrar/Ocultar búsqueda rápida**, para que también funcione ahí. En caso contrario, cambiar de aplicación con alt-tab, o ejecutar la aplicación en modo ventana sin bordes en lugar de pantalla completa exclusiva, evita el problema.

## Los resultados de búsqueda parecen desactualizados

Las unidades NTFS y ReFS se actualizan desde el USN Journal casi en tiempo real; otros sistemas de archivos locales (FAT32, exFAT, ...) se vigilan directamente en busca de cambios, con la misma continuidad. Si algo sigue pareciendo desactualizado (un archivo que acabas de crear no aparece, o un archivo eliminado sigue apareciendo), usa **Reconstruir índice** en la unidad afectada, en **Configuración → Índice → Unidades locales** (o **Unidades de red**).

## Una unidad de red nunca parece actualizarse

Los recursos de red no tienen un USN Journal que Lertaro pueda vigilar, así que se vuelven a escanear según una programación en su lugar. Comprueba el **Modo de actualización** de la unidad en **Configuración → Índice → Unidades de red** — si está configurado en **Manual**, nada se actualiza automáticamente; cámbialo a un intervalo programado, o usa **Reconstruir índice** para actualizar bajo demanda. Para recursos compartidos SMB en NAS con enlaces simbólicos especiales o bucles de carpetas recursivos, el motor de escaneo incluye detección de bucles integrada para evitar duplicados.

## La ventana de vista previa se ve mal / recortada

Esto no debería ocurrir — Lertaro ajusta automáticamente la posición y el tamaño de la ventana de vista previa QuickLook al área utilizable de tu monitor. Si sigues viendo el contenido recortado, prueba **Configuración → General → Vista previa → Restablecer ajustes de la ventana de vista previa** para descartar un valor manual de ancho/alto poco habitual, y asegúrate de tener la última versión.

## Un archivo/carpeta no aparece en absoluto

- Comprueba que no esté excluido — **Configuración → Índice → Reglas de exclusión** admite rutas exactas, patrones glob y expresiones regulares, y cualquiera de los tres podría estar afectándolo sin querer.
- Comprueba que la unidad en la que se encuentra esté habilitada en **Configuración → Índice → Unidades locales/de red**.

## Escribir en chino (u otro idioma con IME) en la ventana en línea no funciona

La [ventana en línea](./getting-started#las-tres-ventanas) deliberadamente nunca le quita el foco real de teclado a la ventana en la que está acoplada — eso es lo que te permite cerrarla y aterrizar exactamente donde estabas, sin parpadeo de foco. Sin embargo, la composición de un IME (la lista de candidatos, y el carácter que realmente confirma) solo ocurre para la ventana que tenga el foco *real*, que siempre es la aplicación acoplada, nunca Lertaro — así que no hay ningún mensaje que Lertaro pueda interceptar para capturar lo que escribiste. Esto es una limitación estructural de cómo funciona la ventana en línea, no un error.

Dos opciones:

- Escribe directamente la romanización en pinyin (sin necesidad de la ventana emergente de candidatos del IME) — Lertaro compara automáticamente los nombres de archivo en chino por pinyin, ver [Sintaxis de búsqueda](./search-syntax#nombres-de-archivo-en-chino-alias-en-pinyin).
- Usa en su lugar la [ventana rápida](./getting-started#las-tres-ventanas) (doble pulsación de `Ctrl` por defecto) — es una ventana con foco real, así que la composición del IME funciona ahí con normalidad.

## ¿Sigues atascado?

Comprueba las pestañas de registro **App**, **Hook** y **Service** en **[Configuración → Estado del Servicio](./settings/service-status)** — el cuadro de búsqueda ahí filtra por palabra clave, y el desplegable de nivel filtra por gravedad — antes de reportar un problema en GitHub.
