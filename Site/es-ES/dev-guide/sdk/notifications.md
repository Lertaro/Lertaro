# Notificaciones

`Lertaro.PluginSdk.Services.PluginNotificationService` permite a un plugin llamar la atención del usuario desde segundo plano a través de las propias ventanas del anfitrión: sin procesos adicionales, sin canal de avisos del sistema operativo y sin acoplamiento al ensamblado de interfaz del anfitrión. Es el anfitrión, no el plugin, quien decide cuánto tiempo permanece cada elemento en pantalla y dónde.

## 1. Puntos de entrada

| Firma | Devuelve | Cuándo usarla |
| :--- | :--- | :--- |
| `INotificationHandle Show(NotificationRequest request)` | un identificador de inmediato | quieres reemplazar o cancelar esta notificación más adelante |
| `Task<NotificationResult> ShowAsync(NotificationRequest request)` | el estado final | solo te importa cómo terminó |
| `bool Show(string title, string text, Action? onClick = null)` | si un anfitrión la aceptó | forma heredada de dos argumentos; un `false` significa que no hay ningún anfitrión conectado, así que queda disponible un respaldo |

Todos los puntos de entrada pueden invocarse con seguridad desde el hilo de segundo plano del propio plugin y **nunca lanzan excepciones** —una excepción que escapara se llevaría el bucle del llamador por delante—. Cuando no hay asignado ningún delegado del anfitrión (un plugin ejecutándose fuera del lanzador) la llamada es un no-op silencioso y la respuesta es un identificador cuya tarea ya ha terminado llevando `Unavailable`; la petición solo se registra, con nivel `Warn`, cuando lo que lanzó la excepción fue el delegado del propio anfitrión.

`INotificationHandle` tiene exactamente dos miembros: `Task<NotificationResult> Completion` y `void Dismiss()`. `Completion` **siempre** termina, incluso cuando nada llegó a la pantalla, así que un `await` sobre ella no puede dejar un hilo en pausa. `Dismiss()` cierra una tarjeta mostrada como si el usuario la hubiera descartado, no hace nada una vez que ha desaparecido y nunca revierte un resultado ya entregado.

Un plugin que necesite una ventana propia usa [`Windows.PluginWindow`](./services); uno que necesite una *respuesta* en lugar de un anuncio usa `PluginMessageBoxService`.

## 2. `NotificationRequest`

| Propiedad | Valor por defecto | Comportamiento |
| :--- | :--- | :--- |
| `string? Id` | `null` | Dos peticiones con el mismo `Id` se dirigen a la misma notificación. Consulta **Reemplazo por Id** (§4). |
| `string Title` | `string.Empty` | Cabecera de la tarjeta. `BottomNotice` la ignora, porque no tiene fila de título. |
| `string Message` | `string.Empty` | Texto del cuerpo. Una petición cuyo `Title` y `Message` estén ambos en blanco o solo con espacios se rechaza como `InvalidRequest`: no hay nada que mostrar. |
| `NotificationLevel Level` | `Info` | `Info` / `Warn` / `Error`. Determina el icono y el color semántico, y nada más. |
| `NotificationPosition Position` | `CardStack` | `CardStack` o `BottomNotice`. Cada una tiene su propia cola y sus propias reglas; nunca se bloquean entre sí. |
| `double? DurationSeconds` | `null` | `null` toma el valor por defecto de esa posición. Un valor explícito fuera del rango de esa posición se **recorta al límite más cercano**, nunca se rechaza; `NaN` o un negativo caen en el extremo más corto. |
| `Action? OnClick` | `null` | Se ejecuta además de cerrar. Consulta **La devolución de llamada del clic** (§6). |

## 3. Las dos posiciones

| | `CardStack` (abajo a la derecha) | `BottomNotice` (centrada abajo) |
| :--- | :--- | :--- |
| Forma | tarjeta con título, nombre del origen, ✕ y "marcar todo como leído" | una sola línea; sin título, sin origen, sin controles |
| Simultáneas | 5 visibles, el resto en cola | 1: una nueva reemplaza a la actual de inmediato, sin espera de cierre |
| Duración por defecto | 8 s | 4 s |
| Rango recortado | 2 – 30 s | 2 – 10 s |
| `OnClick` | se respeta | se ignora (no hay nada interactivo) |
| Una aplicación a pantalla completa exclusiva ocupa la pantalla | se reduce a la línea de aviso, con tope de 5 s | no le afecta |

Otros dos números de cadencia conviene conocerlos cuando se emite una ráfaga:

- El anfitrión ejecuta un único **tick de 100 ms**, y promueve **como máximo una** tarjeta en cola por tick. Una ráfaga españa por tanto sus propias llegadas y, como cada tarjeta cuenta su tiempo desde el momento en que apareció, espaciar las llegadas también espacia los vencimientos: una ráfaga ya no arranca cinco huecos de la pila de un solo movimiento.
- **Un plugin puede tener como máximo 10 peticiones de tarjeta aceptadas** (en pantalla *y* en cola, sumadas). La undécima se descarta como `QueueFull` y se registra. El tope se aplica solo en la ruta de admisión de tarjetas: una petición `BottomNotice` nunca cuenta contra él, porque el aviso es un único hueco que se sobreescribe a sí mismo. No hay un tope global de pendientes entre plugins, así que un plugin no puede quedar hambriento por la ráfaga de otro, pero tampoco un productor desbocado está limitado de forma global.

## 4. Reemplazo por Id

Dar a una petición un `Id` es la forma en que una actualización de estado repetitiva evita llenar la pantalla de copias de sí misma: una petición nueva cuyo `Id` coincida con una tarjeta **visible** sobrescribe esa tarjeta en el sitio —mismo hueco de la pila, sin saltar al principio— y el identificador del llamador anterior completa como `Replaced`. Los llamadores que necesitan saber que fueron sustituidos son precisamente los que guardan el identificador.

Un límite a respetar: la deduplicación se compara con las tarjetas que hay actualmente en pantalla, **no** con las que aún esperan en la cola. Un plugin que dispare cinco copias de un mismo `Id` seguidas mientras la pantalla está llena verá cómo entran en cola las cinco.

## 5. Resultados y motivos de fallo

`NotificationResult` es `Succeeded` más un `NotificationFailure` opcional. El primer estado final que llega a un elemento gana; nada lo reescribe después.

| `NotificationFailure` | Cuándo |
| :--- | :--- |
| `Unavailable` | Ningún anfitrión atendió la petición: un plugin ejecutándose fuera del lanzador, o un delegado del anfitrión que lanzó una excepción. |
| `InvalidRequest` | `Title` y `Message` estaban ambos en blanco o solo con espacios. |
| `QueueFull` | Este plugin ya tenía 10 peticiones de tarjeta aceptadas. |
| `Replaced` | Una petición más reciente llevaba el mismo `Id`. Un reemplazo de `BottomNotice` completa así la petición sustituida **incluso cuando ninguna de las dos lleva `Id`**, porque el aviso es un único hueco que siempre se sobreescribe. |
| `CancelledByPluginUnload` | El plugin fue desactivado o descargado mientras su tarjeta se mostraba o estaba en cola. |
| `HostShuttingDown` | El lanzador se está cerrando y se llevó la notificación consigo. |

Una notificación que llegó a la pantalla y agotó su tiempo, o que fue cerrada por el usuario o por `Dismiss()`, completa como `NotificationResult.Success`: esos son finales, no fallos.

## 6. La devolución de llamada del clic

`OnClick` se ejecuta en **cualquier cierre por parte del usuario**, que el código trata como un único resultado: pulsar el cuerpo de la tarjeta y pulsar ✕ siguen el mismo camino. Una cuenta atrás que simplemente expira **no** la ejecuta, y tampoco ninguno de los fallos del §5.

La devolución de llamada es código del plugin, así que una excepción dentro de ella se captura, se registra con el nombre del origen y nunca se devuelve al pipeline de entrada del lanzador. Haz el trabajo en sí fuera de la devolución de llamada si puede fallar; su misión es llevar al usuario hasta tu ventana (el ejemplo del repositorio es `Plugins/Calendar`, que la usa para activar la vista del calendario).

## 7. La autoría que el plugin no puede falsificar

El nombre impreso en la tarjeta procede de `Assembly.GetCallingAssembly()`, leído en cada pila **pública** de la fachada y después mapeado por el anfitrión: un ensamblado llamado `Lertaro.App*` se muestra como `Lertaro`, un plugin registrado se muestra con su nombre visible gestionado, y cualquier otro caso recae en el nombre del ensamblado con el prefijo `Lertaro.Plugins.` quitado. No se ofrece, de forma deliberada, pasar un nombre de origen como cadena: es la única autoría que un plugin no debe elegir.

## 8. Colocación y superficie

- **Qué pantalla**: la de la ventana en primer plano; si no, la del cursor; si no, la principal. El área de trabajo se convierte a DIP a través de los DPI de ese monitor, así que una tarjeta ocupa el mismo tamaño físico en un panel escalado al 200 % que en uno al 100 %.
- **Orden de la pila**: abajo a la derecha, ordenadas por secuencia de llegada; las tarjetas por encima de una retirada vuelven a deslizarse (es la única animación, ~200 ms con una caída de entrada de 40 DIP).
- **Tamaño**: una tarjeta ocupa como máximo la mitad del alto del área de trabajo (`min(260 DIP, 50 %)`), así que un mensaje largo no puede zamparse un monitor pequeño.
- **Arrastre**: una tarjeta solo se puede arrastrar desde su barra de título, y una tarjeta arrastrada conserva su propia esquina. Un cambio en la configuración de pantalla reancla la pila, lo que descarta el arrastre del usuario: el intercambio más barato que dejar una tarjeta fuera de la pantalla.
- **Tipo de superficie**: se decide una única vez, en la construcción, a partir del `WindowOpacity` del tema activo. Un tema totalmente opaco obtiene una ventana normal cuyos bordes redondea el gestor de ventanas, lo que conserva ClearType; un tema translúcido obtiene una ventana por capa cuyo borde tiene que pintarse y recortarse. Una tarjeta mostrada a través de un cambio de tema conserva, por tanto, el tipo con el que nació hasta que desaparece.
- **Topmost** está siempre activo, y las ventanas quedan excluidas de Alt+Tab (`WS_EX_TOOLWINDOW`). Las notificaciones no se desvanecen: nada anima `Window.Opacity`, porque eso costaría ClearType y una composición píxel a píxel en cada fotograma. La marca de llegada es un destello del borde dentro de una ventana ya opaca (~750 ms).

## 9. Hilos y ciclo de vida

- Invoca desde cualquier hilo. El anfitrión serializa todos los puntos de entrada de notificación en una sola esclusa, así que `Show` es una llamada corta y bloqueante: no la dirijas desde un bucle caliente por elemento; envía en su lugar una tarjeta resumen.
- Las ventanas se presentan con `DispatcherPriority.Background`, así que una ráfaga de notificaciones no puede acaparar el propio trabajo de entrada del lanzador.
- Mientras la **sesión está bloqueada** (salvapantallas, `Win+L`), la cuenta atrás se congela y las tarjetas se ocultan; vuelven cuando la sesión se desbloquea, aún con el tiempo que se les dio.
- Desactivar o descargar el plugin cancela sus tarjetas *en pantalla y* en cola (`CancelledByPluginUnload`); el apagado ordenado del lanzador hace lo mismo con todo lo pendiente (`HostShuttingDown`).
- En esta compilación **no existe ningún ajuste de notificaciones de cara al usuario**: ni no molestar, ni interruptor por plugin, y las reglas de posición y duración de arriba son del anfitrión y no son configurables. Diseña las peticiones en consecuencia: un plugin que habla sin parar debería callarse él mismo, no pedir al usuario que lo silencie.

## 10. Ejemplo

```csharp
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;

// Una tarjeta por recordatorio, reemplazada en lugar de repetida si sigue en pantalla.
var handle = PluginNotificationService.Show(new NotificationRequest
{
    Id = $"reminder-{due.Item.Id}",
    Title = TranslationService.Get("Reminder_Title"),
    Message = due.Item.Text,
    Level = NotificationLevel.Warn,
    DurationSeconds = 12,               // el anfitrión la recorta al rango 2..30
    OnClick = () => CalendarView.ShowOrActivate(),
});

// Esperala solo donde necesites de verdad el estado final; la tarea siempre termina.
_ = handle.Completion.ContinueWith(t =>
{
    if (t.Result.Failure is NotificationFailure.Replaced)
        Logger.Log($"Sustituido por un recordatorio más reciente: {due.Item.Id}", LogLevel.Debug);
});
```

> [!NOTE]
> Las firmas, los valores por defecto y los límites de esta página se leyeron de `PluginSdk/Abstractions/NotificationRequest.cs`, `PluginSdk/Abstractions/INotificationHandle.cs`, `PluginSdk/Services/PluginNotificationService.cs` y de la parte del anfitrión en `App/Services/Notifications/` y `App/Views/Notifications/`.
