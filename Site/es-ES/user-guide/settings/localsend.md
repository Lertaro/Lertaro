# Configuración de LocalSend

Lertaro incluye compatibilidad integrada con el protocolo oficial de [LocalSend](https://localsend.org) para la transferencia de archivos, carpetas y texto entre dispositivos en la misma red local. Accesible en **Configuración → LocalSend**.

## Configuración básica

- **Habilitar transferencia LocalSend** —— Activa o desactiva el servicio LocalSend. Al activarse, el menú de la bandeja del sistema proporciona la opción "Enviar por LocalSend..." y responde al atajo global.
- **Alias del dispositivo** —— Nombre que verán otros dispositivos en la red local. Generado automáticamente a partir del nombre del equipo.
- **Puerto** —— Puerto HTTP/HTTPS para escuchar las solicitudes de transferencia entrantes (por defecto `53317`).

## Seguridad y cifrado

- **Transferencia cifrada (HTTPS)** —— Utiliza conexión cifrada HTTPS para la transferencia de datos (requiere compatibilidad con HTTPS en el dispositivo de destino).
- **PIN de recepción** —— Código PIN opcional necesario para la autorización de transferencias entrantes (dejar en blanco para desactivar).

## Recepción y almacenamiento

- **Guardado rápido (Guardar automáticamente)** —— Acepta y guarda automáticamente los archivos recibidos sin requerir confirmación manual.
- **Ubicación de descarga** —— Directorio de destino por defecto para los archivos recibidos.

## Modos de envío e invocación

- **Invocación por atajo** —— Presiona **`Ctrl+S`** por defecto (configurable en [Atajos](./hotkeys-page)) o selecciona "Enviar por LocalSend..." en el menú de la bandeja.
- **Cambio de modo** —— La ventana de envío permite alternar entre [ Enviar archivos / carpetas ] y [ Enviar texto ].
