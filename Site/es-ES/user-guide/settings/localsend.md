# LocalSend (Configuración)

Lertaro incluye un motor de transmisión inalámbrica local nativamente compatible con el protocolo de código abierto [LocalSend](https://localsend.org). Sin necesidad de internet ni cables, permite compartir archivos, carpetas y texto plano a máxima velocidad entre ordenadores Windows, Mac, Linux, iPhone, iPad y dispositivos Android en la misma red local. La página se encuentra en **Configuración → LocalSend**.

## 1. Configuración básica del servicio

- **Habilitar transmisión LocalSend**: Interruptor maestro. Al activarlo, Lertaro inicia un servicio de escucha en segundo plano y añade la opción "Enviar a otros dispositivos..." al menú de la bandeja del sistema.
- **Nombre de dispositivo**: El alias visible para otros dispositivos en la red local. Se genera a partir del nombre del equipo y puede cambiarse (p. ej., `Portátil de trabajo`).
- **Puerto del servicio**: Puerto de escucha HTTP/HTTPS (por defecto `53317`, estándar de LocalSend). Modificable si entra en conflicto con otros servicios.

## 2. Seguridad, cifrado y almacenamiento

- **Transferencia cifrada (HTTPS)**: Activado por defecto. Utiliza TLS en las comunicaciones locales para evitar interceptaciones de paquetes.
- **Código PIN de recepción**: PIN numérico opcional (4–6 dígitos). Si se activa, el emisor debe introducir el mismo PIN antes de iniciar la transferencia (dejar vacío para desactivar).
- **Guardar archivos automáticamente**: Acepta y guarda transferencias de dispositivos locales automáticamente sin requerir confirmación manual.
- **Directorio de guardado**: Carpeta de destino predeterminada para los archivos recibidos (por defecto `Downloads\LocalSend`), personalizable mediante el botón Examinar.

## 3. Ventana de envío y flujo de trabajo

- **Invocación por atajo**: Pulsa el atajo global predeterminado **`Ctrl+S`** (reasignable en [**Configuración → Atajos de teclado**](./hotkeys-page)) para abrir la ventana de envío de LocalSend.
- **Modos de envío**: Alterna fácilmente entre los modos **[Enviar archivos / carpetas]** y **[Enviar texto]** en la parte superior.
- **Envío por arrastre**: Arrastra archivos directamente desde los resultados de Lertaro, el Panel rápido o el Explorador hacia la ventana. El radar detecta automáticamente los dispositivos en línea; pulsa sobre el destinatario para transferir los archivos.
