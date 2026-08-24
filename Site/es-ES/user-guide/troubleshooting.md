# Solución de problemas

¿Tienes dificultades al usar Lertaro? Sigue estos pasos sistemáticos para identificar y resolver las causas más comunes.

## 1. Los atajos globales no responden

- **Comprobar el estado del servicio**: Accede a [**Configuración → Estado del servicio**](./settings/service-status) y confirma que `Lertaro.Service` está en ejecución. Aunque los atajos ordinarios los gestiona la App, el proceso elevado de intercepción depende del servicio.
- **Aislamiento de privilegios (UIPI)**: Si la ventana en primer plano se ejecuta como administrador (p. ej., símbolo del sistema o Administrador de tareas elevados), Windows bloquea la intercepción de teclas desde procesos estándar. Lertaro eleva automáticamente su interceptor; asegúrate de que el servicio esté activo.
- **Revisar la lista negra de procesos**: En [**Configuración → Atajos de teclado**](./settings/hotkeys-page#lista-negra-de-procesos), comprueba si la aplicación activa está en la lista negra. Lertaro ignora intencionadamente los atajos mientras estas apps están en primer plano.
- **Omisión en juegos y apps a pantalla completa**: Con juegos 3D o reproductores a pantalla completa exclusiva, los atajos se omiten por defecto para no interferir. Activa **Responder al enfocar aplicaciones a pantalla completa** en **Configuración → Atajos de teclado** si prefieres que respondan siempre.

## 2. Los resultados están desactualizados o incompletos

- **Unidades NTFS / ReFS locales**: Se actualizan casi en tiempo real leyendo el diario de cambios USN del sistema de archivos.
- **Unidades FAT32 / exFAT**: Se supervisan continuamente mediante eventos de cambio del sistema de archivos.
- **Reconstrucción manual**: Si un corte de energía provocó inconsistencias en el índice, ve a [**Configuración → Indexación → Unidades locales**](./settings/index-drives) y pulsa **Reconstruir índice** en la unidad afectada.

## 3. Las unidades de red no se actualizan

- **Escaneo periódico**: Los recursos de red compartidos (SMB / NAS) carecen de diario USN local y dependen de sondeos programados.
- **Comprobar el modo de actualización**: En [**Configuración → Indexación → Unidades de red**](./settings/index-drives#unidades-de-red), verifica que no esté en modo "Manual".
- **Protección contra bucles de enlaces simbólicos**: Lertaro incluye detección de bucles para evitar bloqueos por enlaces recursivos en carpetas NAS.

## 4. Un archivo o carpeta no aparece en las búsquedas

- **Revisar reglas de exclusión**: Ve a [**Configuración → Indexación → Reglas de exclusión**](./settings/index-drives#reglas-de-exclusión) y confirma que no esté excluido por ruta exacta, comodines o expresiones regulares.
- **Comprobar activación de unidad**: En [**Configuración → Indexación → Unidades locales**](./settings/index-drives), confirma que la unidad esté habilitada.

## 5. El menú de candidatos IME no aparece en la Ventana incrustada

- **Diseño sin foco**: La [Ventana incrustada](./getting-started#3-tres-modalidades-de-ventana) no toma el foco del teclado para evitar parpadeos al cerrarse. Dado que los menús de candidatos de ciertos métodos de entrada (IME) requieren foco real de ventana, pueden no mostrarse en modo incrustado.
- **Solución recomendada**: Usa la Ventana rápida (doble pulsación de `Ctrl`), la cual cuenta con foco completo.

## 6. Consulta de registros y reporte de errores

Si el problema persiste, revisa los registros en [**Configuración → Estado del servicio**](./settings/service-status):

- **Registros de Service**: Indexación, diarios USN, escaneos de red y comunicación IPC.
- **Registros de App**: Renderizado de interfaz, plugins, atajos y cambios de configuración.
- **Registros de Hook**: Intercepción de teclado y gestos de ratón.

Filtra por palabras clave o nivel de gravedad (Info / Warn / Error) antes de adjuntar los registros a un issue en GitHub.
