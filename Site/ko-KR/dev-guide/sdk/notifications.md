# 알림

`Lertaro.PluginSdk.Services.PluginNotificationService`로 플러그인은 호스트 자신의 창을 통해 백그라운드에서 사용자의 주의를 끌 수 있습니다 — 별도 프로세스도, OS 토스트 채널도, 호스트 UI 어셈블리 결합도 없습니다. 무언가가 화면에 얼마나 오래, 어디에 머무르는지는 플러그인이 아니라 호스트가 결정합니다.

## 1. 진입점

| 시그니처 | 반환 | 사용 시점 |
| :--- | :--- | :--- |
| `INotificationHandle Show(NotificationRequest request)` | 핸들을 즉시 반환 | 이 알림을 나중에 대체하거나 취소해야 할 때 |
| `Task<NotificationResult> ShowAsync(NotificationRequest request)` | 종단 상태 | 어떻게 끝났는지만 알고 싶을 때 |
| `bool Show(string title, string text, Action? onClick = null)` | 호스트가 받아들였는지 여부 | 레거시 두 인수 형태. `false`는 연결된 호스트가 없다는 뜻이므로 대체 수단을 쓸 수 있습니다 |

모든 진입점은 플러그인 자신의 백그라운드 스레드에서 호출해도 안전하며 **예외를 던지지 않습니다** — 빠져나간 예외는 호출자의 루프까지 함께 무너뜨립니다. 호스트 델리게이트가 아예 대입되지 않은 경우(런처 밖에서 실행되는 플러그인)에는 호출이 조용히 아무 일 없이 끝나고, 이미 끝난 상태로 `Unavailable`을 실은 핸들이 답으로 돌아옵니다. 요청이 `Warn` 수준으로 기록되는 것은 호스트 자신의 델리게이트가 예외를 던졌을 때뿐입니다.

`INotificationHandle`의 멤버는 정확히 두 개입니다: `Task<NotificationResult> Completion`과 `void Dismiss()`. `Completion`은 화면에 아무것도 닿지 않은 경우를 포함해 **항상** 끝나므로, 그것을 `await`한다고 스레드가 매어 있지는 않습니다. `Dismiss()`는 사용자가 닫은 것처럼 표시된 카드를 닫고, 이미 사라진 뒤에는 아무 동작이 없으며, 이미 전달된 결과를 되돌리지는 않습니다.

자신이 소유한 창이 필요한 플러그인은 [`Windows.PluginWindow`](./services)를 쓰고, 알림이 아니라 *답변*이 필요한 플러그인은 `PluginMessageBoxService`를 씁니다.

## 2. `NotificationRequest`

| 속성 | 기본값 | 동작 |
| :--- | :--- | :--- |
| `string? Id` | `null` | 같은 `Id`를 가진 두 요청은 같은 알림을 가리킵니다. **`Id`로 대체**(§4) 참조. |
| `string Title` | `string.Empty` | 카드 헤더. 제목 행이 없는 `BottomNotice`는 무시합니다. |
| `string Message` | `string.Empty` | 본문 텍스트. `Title`과 `Message`가 **모두** 비어 있거나 공백뿐인 요청은 보여 줄 것이 없으므로 `InvalidRequest`로 거절됩니다. |
| `NotificationLevel Level` | `Info` | `Info` / `Warn` / `Error`. 아이콘과 의미 색상만 결정하고 그 밖에는 영향이 없습니다. |
| `NotificationPosition Position` | `CardStack` | `CardStack` 또는 `BottomNotice`. 각각 자체 대기열과 자체 규칙을 가지며 서로를 절대 막지 않습니다. |
| `double? DurationSeconds` | `null` | `null`이면 그 위치 자체의 기본값을 씁니다. 위치 범위를 벗어난 명시 값은 **가까운 경계로 클램프**될 뿐 거절되지 않으며, `NaN`이나 음수는 더 짧은 쪽으로 떨어집니다. |
| `Action? OnClick` | `null` | 닫히는 것 외에 추가로 실행됩니다. **클릭 콜백**(§6) 참조. |

## 3. 두 위치

| | `CardStack` (우측 하단) | `BottomNotice` (하단 중앙) |
| :--- | :--- | :--- |
| 형태 | 제목과 보낸 주체와 ✕와 "모두 읽음"이 있는 카드 | 한 줄; 제목도 보낸 주체도 컨트롤도 없음 |
| 동시 표시 | 5개 표시, 나머지는 대기열 | 1개 — 새 알림이 정리 대기 없이 현재 알림을 즉시 대체 |
| 기본 표시 시간 | 8초 | 4초 |
| 클램프 범위 | 2 – 30초 | 2 – 10초 |
| `OnClick` | 반영됨 | 무시됨 (인터랙티브 요소 없음) |
| 독점 전체 화면 앱이 화면을 차지할 때 | 한 줄 알림으로 줄어들며 상한 5초 | 영향 없음 |

한꺼번에 많은 알림을 보낼 때 알아 두면 좋은 추가 속도 값 두 개:

- 호스트는 **100 ms 틱**을 하나로 돌리고, 틱마다 대기 중인 카드를 **최대 하나** 올립니다. 그래서 폭주하는 알림은 도착 자체가 간격을 두게 되고, 각 카드는 화면에 나타난 순간부터 시간을 세므로 도착 간격이 만료 간격도 됩니다 — 한꺼번에 보내도 스택 슬롯 다섯 개를 한 동작으로 차지하지 않게 됩니다.
- **플러그인 하나는 받아들여진 카드 요청을 최대 10개**까지만 가질 수 있습니다(화면 위 *및* 대기 중 합산). 열한 번째는 `QueueFull`로 버려지고 기록됩니다. 이 상한은 카드 수용 경로에서만 적용됩니다 — `BottomNotice` 요청은 이 상한으로 절대 계산되지 않습니다. 한 줄 알림은 자기 자신을 덮어쓰는 단일 슬롯이기 때문입니다. 플러그인 사이를 넘는 전역 대기 상한은 없어서 다른 플러그인의 폭주에 굶주리지는 않지만, 폭주하는 생성자가 전역으로 제한되지도 않습니다.

## 4. `Id`로 대체

요청에 `Id`를 주는 것은 반복되는 상태 업데이트가 화면을 자기 복사로 채우지 않게 하는 방법입니다: **표시된** 카드와 `Id`가 일치하는 새 요청이 들어오면 그 카드를 그 자리에서 덮어씁니다 — 스택에서 같은 슬롯, 맨 앞으로 올리지 않음 — 이전 호출자의 핸들은 `Replaced`로 끝납니다. 자기가 대체되었는지 알아야 하는 호출자는 바로 핸들을 간직하는 쪽입니다.

지켜야 할 제한 하나: 중복 제거는 현재 화면에 있는 카드와 대조하며, 대기열에 아직 남아 있는 카드와는 **대조하지 않습니다**. 화면이 가득 찬 상태에서 같은 `Id`로 다섯 번 연달아 보내면 다섯 개가 모두 대기열에 들어갑니다.

## 5. 결과와 실패 사유

`NotificationResult`는 `Succeeded`에 선택적 `NotificationFailure`를 더한 형태입니다. 항목에 도달한 첫 종단 상태가 이기며, 그 뒤에는 아무것도 그것을 다시 쓰지 않습니다.

| `NotificationFailure` | 발생 조건 |
| :--- | :--- |
| `Unavailable` | 호스트가 요청을 처리하지 않음 — 런처 밖에서 실행되는 플러그인, 또는 예외를 던진 호스트 델리게이트. |
| `InvalidRequest` | `Title`과 `Message`가 모두 비어 있거나 공백뿐인 경우. |
| `QueueFull` | 이 플러그인에 이미 받아들여진 카드 요청이 10개인 경우. |
| `Replaced` | 더 새로운 요청이 같은 `Id`를 가진 경우. `BottomNotice`가 덮어써질 때는 두 요청 모두 `Id`를 **가지지 않았더라도** 대체된 요청이 이 방식으로 끝납니다. 한 줄 알림은 항상 자기 슬롯을 덮어쓰기 때문입니다. |
| `CancelledByPluginUnload` | 카드 표시 중이거나 대기 중에 플러그인이 비활성화·언로드된 경우. |
| `HostShuttingDown` | 런처가 종료되며 함께 알림을 가져간 경우. |

화면에 도달해 주어진 시간을 다 채운 알림, 또는 사용자나 `Dismiss()`로 닫힌 알림은 `NotificationResult.Success`로 끝납니다 — 이들은 실패가 아니라 종료입니다.

## 6. 클릭 콜백

`OnClick`은 **모든 사용자 닫기**에서 실행됩니다. 코드 관점에서 카드 본문 클릭과 ✕ 클릭은 같은 경로를 택하는 하나의 결과입니다. 카운트다운이 단순히 만료될 때는 실행되지 않으며, §5의 어떤 실패도 실행하지 않습니다.

콜백은 플러그인 코드이므로 안에서 발생한 예외는 잡혀서 보낸 주체와 함께 기록되고, 런처의 입력 파이프라인으로 되돌아가지는 않습니다. 실패할 수 있는 작업 자체는 콜백 밖에서 처리하세요. 콜백의 임무는 사용자를 플러그인 창으로 데려오는 것입니다(저장소 안 예제는 `Plugins/Calendar`이며, 달력 뷰를 활성화하는 데 씁니다).

## 7. 플러그인이 위조할 수 없는 표기

카드에 찍히는 이름은 `Assembly.GetCallingAssembly()`에서 나오며, 퍼사드의 각 **공개** 프레임에서 읽은 뒤 호스트가 매핑합니다: `Lertaro.App*` 이름의 어셈블리는 `Lertaro`로 표시되고, 등록된 플러그인은 관리형 표시 이름으로, 그 밖은 `Lertaro.Plugins.` 접두어를 뺀 어셈블리 이름으로 물러납니다. 보낸 주체를 문자열로 전달하는 방법은 의도적으로 제공하지 않았습니다 — 플러그인이 선택하면 안 되는 단 하나의 표식이기 때문입니다.

## 8. 배치와 서피스

- **어떤 화면**: 전경 창이 있는 화면, 없으면 커서가 있는 화면, 그것도 없으면 주 화면. 작업 영역은 그 모니터의 DPI로 DIP 환산되므로, 카드는 200 % 확대 패널이든 100 % 패널이든 같은 물리 크기를 가집니다.
- **스택 순서**: 우측 하단에 도착 순서대로; 제거된 카드 위에 있던 카드는 다시 미끄러져 올라옵니다(유일한 애니메이션이며 약 200 ms, 40 DIP 내려오는 진입 이동).
- **크기**: 카드는 작업 영역 높이의 절반까지(`min(260 DIP, 50 %)`) 차지하므로 긴 메시지가 작은 모니터를 집어삼킬 수 없습니다.
- **드래그**: 카드는 제목 표시줄로만 옮길 수 있고, 옮긴 카드는 자기 모서리를 유지합니다. 디스플레이 설정이 바뀌면 스택이 새 기준으로 재배치되면서 사용자의 드래그는 사라집니다 — 카드를 화면 밖에 남겨 두는 것보다 싼 교환입니다.
- **서피스 종류**: 생성 시점에 활성 테마의 `WindowOpacity`로 한 번 결정됩니다. 완전 불투명 테마는 창 관리자가 모서리를 둥글게 처리하는 일반 창을 얻어 ClearType이 유지되고, 반투명 테마는 모서리를 직접 그리고 클리핑해야 하는 레이어드 창을 얻습니다. 그래서 테마가 바뀌는 동안에도 표시 중인 카드는 사라질 때까지 처음에 만들어진 종류를 유지합니다.
- **Topmost**는 항상 켜져 있고, 창은 Alt+Tab에서 제외됩니다(`WS_EX_TOOLWINDOW`). 알림은 페이드하지 않습니다: `Window.Opacity`를 애니메이션하는 것이 없는데, 그러려면 ClearType을 대가로 치르고 매 프레임 픽셀 단위 합성을 해야 있기 때문입니다. 도착 표시는 이미 불투명한 창 안에서의 테두리 점멸입니다(약 750 ms).

## 9. 스레딩과 생명주기

- 어떤 스레드에서든 호출하세요. 호스트는 모든 알림 진입점을 하나의 게이트에서 직렬화하므로 `Show`는 짧게 막히는 호출입니다 — 항목별 핫 루프에서 돌리지 말고 요약 카드 하나로 보내세요.
- 창은 `DispatcherPriority.Background`로 표시되므로, 알림 폭주가 런처 자신의 입력 작업을 굶주리게 하지는 않습니다.
- **세션이 잠긴** 동안(화면 보호기, `Win+L`)에는 카운트다운이 얼어붙고 카드가 숨겨지며, 잠금이 풀리면 받은 시간을 그대로 들고 돌아옵니다.
- 플러그인을 비활성화하거나 언로드하면 표시 중인 *및* 대기 중인 카드가 취소되고(`CancelledByPluginUnload`), 런처의 정상 종료는 남아 있는 전부에게 같은 일을 합니다(`HostShuttingDown`).
- 이 빌드에는 **사용자용 알림 설정이 전혀 없습니다**: 방해 금지도, 플러그인별 스위치도 없고, 위의 위치와 표시 시간 규칙은 호스트의 것이며 조정할 수 없습니다. 요청을 그렇게 설계하세요 — 계속 떠드는 플러그인은 사용자에게 음소거를 요청할 것이 아니라 스스로 조용해져야 합니다.

## 10. 예제

```csharp
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;

// 리마인더당 카드 하나, 아직 화면에 남아 있다면 반복하지 않고 대체한다.
var handle = PluginNotificationService.Show(new NotificationRequest
{
    Id = $"reminder-{due.Item.Id}",
    Title = TranslationService.Get("Reminder_Title"),
    Message = due.Item.Text,
    Level = NotificationLevel.Warn,
    DurationSeconds = 12,               // 호스트가 2..30 안으로 클램프한다
    OnClick = () => CalendarView.ShowOrActivate(),
});

// 정말로 종단 상태가 필요할 때만 대기한다; 작업은 항상 끝난다.
_ = handle.Completion.ContinueWith(t =>
{
    if (t.Result.Failure is NotificationFailure.Replaced)
        Logger.Log($"더 새로운 리마인더에 대체됨: {due.Item.Id}", LogLevel.Debug);
});
```

> [!NOTE]
> 이 페이지의 시그니처와 기본값과 제한은 `PluginSdk/Abstractions/NotificationRequest.cs`, `PluginSdk/Abstractions/INotificationHandle.cs`, `PluginSdk/Services/PluginNotificationService.cs`, 그리고 `App/Services/Notifications/`와 `App/Views/Notifications/` 아래 호스트 측 코드를 보고 확인한 것입니다.
