# Solución de problemas

¿Tienes dificultades al usar Lertaro? Sigue estos pasos sistemáticos para identificar y resolver las causas más comunes.

## 1. Los atajos globales no responden

- **Comprobar el estado del servicio**: Accede a [**Configuración → Estado del servicio**](./settings/service-status) y confirma que el servicio de indexación en segundo plano está en ejecución. Aunque los atajos ordinarios los gestiona la App, el proceso elevado de intercepción depende del servicio.
- **Aislamiento de privilegios (UIPI)**: Si la ventana en primer plano se ejecuta como administrador (p. ej., símbolo del sistema o Administrador de tareas elevados), Windows bloquea la intercepción de teclas desde procesos estándar. Lertaro eleva automáticamente su interceptor; asegúrate de que el servicio esté activo.
- **Revisar la lista negra de procesos**: En [**Configuración → Atajos de teclado**](./settings/hotkeys-page#_3-lista-negra-de-procesos), comprueba si la aplicación activa está en la lista negra. Lertaro ignora intencionadamente los atajos mientras estas apps están en primer plano.
- **Omisión en juegos y apps a pantalla completa**: Con juegos 3D o reproductores a pantalla completa exclusiva, los atajos se omiten por defecto para no interferir. Activa **Responder al enfocar aplicaciones a pantalla completa** en **Configuración → Atajos de teclado** si prefieres que respondan siempre.

## 2. Los resultados están desactualizados o incompletos

- **Unidades NTFS / ReFS locales**: Se actualizan casi en tiempo real leyendo el diario de cambios USN del sistema de archivos.
- **Unidades FAT32 / exFAT**: Se supervisan continuamente mediante eventos de cambio del sistema de archivos.
- **Reconstrucción manual**: Si un corte de energía provocó inconsistencias en el índice, ve a [**Configuración → Indexación → Unidades locales**](./settings/index-drives) y pulsa **Reconstruir índice** en la unidad afectada.

## 3. Las unidades de red no se actualizan

- **Escaneo periódico**: Los recursos de red compartidos (SMB / NAS) carecen de diario USN local y dependen de sondeos programados.
- **Comprobar el modo de actualización**: En [**Configuración → Indexación → Unidades de red**](./settings/index-drives#_2-unidades-de-red), verifica que no esté en modo "Manual".
- **Protección contra bucles de enlaces simbólicos**: Lertaro incluye detección de bucles para evitar bloqueos por enlaces recursivos en carpetas NAS.

## 4. Un archivo o carpeta no aparece en las búsquedas

- **Revisar reglas de exclusión**: Ve a [**Configuración → Indexación → Reglas de exclusión**](./settings/index-drives#_5-reglas-de-exclusion) y confirma que no esté excluido por ruta exacta, comodines o expresiones regulares.
- **Comprobar activación de unidad**: En [**Configuración → Indexación → Unidades locales**](./settings/index-drives), confirma que la unidad esté habilitada.

## 5. El menú de candidatos IME no aparece en la Ventana incrustada

- **Diseño sin foco**: La [Ventana incrustada](./getting-started#_3-tres-modalidades-de-ventana) no toma el foco del teclado para evitar parpadeos al cerrarse. Dado que los menús de candidatos de ciertos métodos de entrada (IME) requieren foco real de ventana, pueden no mostrarse en modo incrustado.
- **Soluciones recomendadas**:
  1. **Escribir pinyin directamente**: Lertaro incorpora un motor de alias por pinyin; basta escribir las letras del pinyin para que coincida con nombres de archivo en chino sin abrir el menú del método de entrada (consulta [**Sintaxis de búsqueda**](./search-syntax#_7-alias-multilingues)).
  2. **Cambiar a la Ventana rápida**: la doble pulsación de `Ctrl` abre la Ventana rápida con foco completo, donde todos los métodos de entrada funcionan con normalidad.

## 6. Directorio de datos, permisos y actualizaciones fallidas

- **Trasladar el directorio de datos**: Mover `%LocalAppData%\Lertaro` (versión instalable) o la carpeta `Data\Users` de la versión portátil a otra partición con un vínculo simbólico o una junction es compatible, pero el destino debe ser un **volumen fijo local**. Un vínculo a una ubicación de red o a un volumen extraíble se rechaza al arrancar indicando el motivo (ver [**Configuración → Acerca de**](./settings/about)).
- **Arranque rechazado: la carpeta de datos pertenece a otra identidad**: Si el propietario del directorio de datos de usuario no es tu cuenta, Lertaro se niega a arrancar e intenta primero una reparación con permisos elevados. El mensaje indica el **propietario real y el SID esperado**, que es todo el diagnóstico. Cada paso de esa reparación —incluido el paso en el que se quedó si se agotó el tiempo— se escribe en `%TEMP%\Lertaro\repair.log`; adjunta ese archivo al informar del problema.

Si el propietario de la carpeta es `BUILTIN\Administrators` pero sus permisos ya conceden FullControl a **tu** cuenta, basta con restablecer el propietario. El script siguiente cambia únicamente la propiedad dentro de `%LOCALAPPDATA%\Lertaro`: no toca los permisos ni el contenido de los archivos.

```powershell
$sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
$root = Join-Path $env:LOCALAPPDATA 'Lertaro'
Get-Item -LiteralPath $root | ForEach-Object {
    $acl = $_.GetAccessControl('Owner')
    $acl.SetOwner($sid)
    $_.SetAccessControl($acl)
}
Get-ChildItem -LiteralPath $root -Recurse -Force | ForEach-Object {
    $acl = $_.GetAccessControl('Owner')
    $acl.SetOwner($sid)
    $_.SetAccessControl($acl)
}
```

- **La última actualización silenciosa falló**: El actualizador escribe su veredicto —estado, motivo y carpeta de reversión— en `update-result.json`, junto a la instalación. Lertaro muestra ese motivo una vez en el siguiente arranque y no repite el mismo veredicto; si el aviso aparece una y otra vez, elimina ese archivo o instala la nueva versión manualmente sobre la actual.
- **Los vínculos por debajo del directorio de datos se rechazan siempre**: Solo la propia cadena del directorio de datos puede resolverse como vínculo; los subdirectorios de su interior no pueden serlo (ver [**Configuración → Acerca de**](./settings/about)).

## 7. La aplicación se cierra justo después de iniciarla

- **La instalación termina y no aparece ninguna ventana**: en Windows 10 22H2 (compilación 19045) y anteriores, un procesador con CET por hardware (Intel de 12.ª generación o posterior, AMD Zen 3 o posterior) hace que .NET aborte antes de ejecutar código propio de Lertaro, porque esas versiones del sistema no admiten por completo el CET en modo de usuario. El Visor de eventos registra una entrada de **.NET Runtime** con el texto *"Your Windows doesn't fully support CET. Please install all available Windows updates"*, y Lertaro no escribe ningún registro: el proceso termina antes de que el registro comience.
- **El paquete de instalación lo repara él solo**: el asistente lee el número de compilación de Windows y, en Windows 10 22H2 y anteriores, elimina la directiva de pila de sombra en modo de usuario de los tres ejecutables de esa carpeta de instalación. No afecta a nada más del equipo, y volver a ejecutar el asistente la aplica de nuevo.
- **Edición portátil**: el instalador no alcanza la carpeta portátil, así que abre PowerShell como administrador, sustituye la ruta por tu propia carpeta y ejecuta una vez estos tres comandos.

```powershell
Set-ProcessMitigation -Name 'D:\Lertaro\Lertaro.App.exe' -Disable UserShadowStack
Set-ProcessMitigation -Name 'D:\Lertaro\Lertaro.Service.exe' -Disable UserShadowStack
Set-ProcessMitigation -Name 'D:\Lertaro\lff.exe' -Disable UserShadowStack
```

- **Aplicar todas las actualizaciones de Windows disponibles** es la otra solución, y es justo lo que pide el mensaje de .NET. Si la aplicación sigue cerrándose de inmediato, recoge los registros como se describe en la sección siguiente y abre una incidencia.

## 8. Consulta de registros y reporte de errores

Si el problema persiste, revisa los registros en [**Configuración → Estado del servicio**](./settings/service-status):

- **Registros de Service**: Indexación, diarios USN, escaneos de red y comunicación IPC.
- **Registros de App**: Renderizado de interfaz, plugins, atajos y cambios de configuración.
- **Registros de Hook**: Intercepción de teclado y gestos de ratón.

Filtra por palabras clave o nivel de gravedad (Info / Warn / Error) antes de adjuntar los registros a un issue en GitHub.
