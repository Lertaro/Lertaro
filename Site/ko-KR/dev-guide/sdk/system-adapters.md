# 시스템 및 대화상자 어댑터

이 장에서는 Windows 파일 탐색기, 네이티브 파일 대화상자, 서드파티 파일 관리자 전반에서 창 도킹, 활성 디렉터리 추출, 인라인 검색 통합을 수행하기 위한 `Lertaro.PluginSdk` 어댑터 인터페이스를 다룹니다.

네 인터페이스는 모두 `Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters`에 위치하고 `IPluginComponent`에서 파생됩니다. **설정 → 플러그인**에 나열되는 `Name`이 여기서 나오며, 이 인터페이스들 스스로 `Name`을 선언하지는 않습니다.

> [!NOTE]
> `IActivePathCollector`, `IFileDialogAdapter`, `IInlineSearchAdapter` 구현체는 관리자 권한으로 실행되는 창과 통신할 때 Windows UIPI 격리를 우회하기 위해 호스트에 의해 **특권 Hook 보조 프로세스**로 로드됩니다. 그래서 이 멤버들은 저렴하고 비대화적으로 동작해야 합니다. 저수준 키보드/마우스 후크 콜백에서 실행되며, 시스템의 `LowLevelHooksTimeout`을 넘는 지연은 후크를 조용히 떨어뜨리기 때문입니다.

## 1. 열린 폴더 수집기 `IOpenedFolderCollector`

계약의 읽기 전용 절반입니다: 대상 관리자가 현재 열어 둔 모든 폴더를 보고합니다. [**퀵 내비게이션**](../../user-guide/hotkeys)과 인라인 검색 목록의 **현재 열린 폴더** 그룹이 여기서 채워집니다.

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

public readonly record struct OpenedFolder(string Path, IntPtr WindowHandle);

public interface IOpenedFolderCollector : IPluginComponent
{
    IReadOnlyList<OpenedFolder> GetOpenedFolders() => Array.Empty<OpenedFolder>();
}
```

어댑자는 열린 창 하나당 항목 하나를 반환하므로, 탭이 다섯 개인 관리자는 폴더 다섯 개를 보고합니다. 레지스트리는 그 목록을 **중복을 의도적으로 남긴 채** 그대로 넘깁니다 — 두 수집기가 본 폴더, 또는 하나의 수집기가 두 번 본 폴더는 두 번 나타나고, 집합이 필요한 호출 측이 경로 기준으로 직접 중복을 제거합니다.

## 2. 활성 경로 수집기 `IActivePathCollector`

`IActivePathCollector`는 `IOpenedFolderCollector`를 **확장**합니다: 특정 창의 폴더를 지목할 수 있는 수집기는 열린 폴더 목록도 함께 제공하는 편이 자연스럽고, 상속된 기본 구현만 따르면 그것을 공짜로 얻습니다.

포커스된 전경 창에서 활성 작업 디렉터리를 추출하여 Lertaro가 인라인 검색 범위를 정하거나 상대 경로를 해석할 수 있게 합니다:

```csharp
public interface IActivePathCollector : IOpenedFolderCollector
{
    string TargetName { get; }   // 대상 파일 관리자 이름 (예: "Directory Opus", "Total Commander")

    // 뒤로 갈수록 더 넓은 질문을 하는 세 오버로드. 클래스명 형태만 필수이고 나머지는 그것을
    // 기본 구현으로 따르므로, 아직 창을 구분하지 못하는 수집기도 모든 호출자에게 올바르게 답한다.
    bool CanHandle(string className);
    bool CanHandle(string windowClassName, string windowTitle) => CanHandle(windowClassName);
    bool CanHandle(IntPtr windowHwnd, string windowClassName, string processName) => CanHandle(windowClassName);

    string? TryGetPath(
        IntPtr activeHwnd, string activeClassName,   // 포커스된 컨트롤
        IntPtr windowHwnd, string windowClassName,   // 그 최상위 창
        string processName);
}
```

- 포커스된 컨트롤과 상위 창이 분리되어 전달되므로, 창 전체가 아니라도 주소 표시줄이나 트리 뷰 같은 중첩 컨트롤에서 경로를 읽어낼 수 있습니다.
- 창은 인식했지만 그 시점에 폴더를 확인할 수 없을 때는 `null`을 반환합니다. 실패가 아니며, 호스트는 이전 범위를 그대로 둡니다.

## 3. 네이티브 파일 대화상자 어댑터 `IFileDialogAdapter`

Windows의 표준 열기 / 저장 / 찾아보기 대화상자를 검사하고 제어합니다:

```csharp
public interface IFileDialogAdapter : IPluginComponent
{
    bool CanHandle(IntPtr hwnd, string className, string processName);
    string? GetCurrentPath(IntPtr hwnd);
    bool NavigateTo(IntPtr hwnd, string targetPath);

    bool TargetIsFolderOnly => false;  // 대상 입력이 폴더만 받는 경우 True (예: 압축 해제 대화상자)
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => true;

    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);          // 카드를 도킹할 위치

    // 배치 프로브: 대화상자 자신의 대상 필드가 어디 있고 파일 목록이 어디 있는지를 알려 준다.
    // 인라인 카드는 이 둘을 읽어 자기 위치를 정한다 -- 필드 아래, 목록 위, 또는 아래에 자리가
    // 없을 때는 들어갈 수 있는 곳. 어느 쪽이든 false를 반환하면 호스트는 GetDockBounds로
    // 물러난다. 둘 다 "그 컨트롤은 확인할 수 없다"가 기본값이다.
    bool TryGetTargetFieldBounds(IntPtr hwnd, out AdapterRect bounds) { bounds = default; return false; }
    bool TryGetFileListBounds(IntPtr hwnd, out AdapterRect bounds) { bounds = default; return false; }

    bool RestoreFocus(IntPtr hwnd);
}

public struct AdapterRect   // 물리 픽셀
{
    public int Left, Top, Right, Bottom;
}
```

- **`TargetIsFolderOnly`**: `true`이면 사용자가 검색 결과에서 파일을 선택했을 때 호스트가 `NavigateTo`를 호출하기 전에 부모 폴더를 자동으로 해석합니다.
- **`TryGetTargetFieldBounds` / `TryGetFileListBounds`**: 카드 배치 전용입니다. 배치기는 카드를 대화상자의 대상 필드 아래에 거는 것을 선호하고 파일 목록을 대체 기준점으로 쓰며, 어댑자가 둘 중 어느 것도 확인할 수 없는 대화상자는 단순히 `GetDockBounds` 사각형을 받습니다.
- **`RestoreFocus`**: 키보드를 대화상자 자신의 입력 필드로 돌려줍니다. 사용자가 인라인 카드를 떠날 때(`Escape`, 또는 검색어가 빈 카드에서 소환 단축키를 다시 누른 경우) 호스트가 호출하므로, 이 메서드는 다른 것을 활성화하면 안 됩니다.

## 4. 인라인 검색 어댑터 `IInlineSearchAdapter`

Lertaro 검색 카드를 대상 파일 대화상자나 파일 탐색기 창에 임베드하여 양방향 선택 동기화를 유지합니다:

```csharp
public interface IInlineSearchAdapter : IPluginComponent
{
    bool IsFileExplorer => false;      // Windows 파일 탐색기인 경우 True

    bool CanHandle(IntPtr hwnd, string className, string processName);

    // 트리거하지 않는 인식. 기본값은 CanHandle이며, 지원 대상 호스트는 분명하지만 카드를
    // 소환해서는 안 되는 경우에 재정의한다 -- 예: 명령 줄이나 이름 변경 입력란에 포커스가 있어
    // 타이핑이 Lertaro가 아니라 그쪽에 속하는 경우.
    bool CanRecognizeHost(IntPtr hwnd, string className, string processName) => CanHandle(hwnd, className, processName);

    bool CanTrigger(IntPtr focusedHwnd, string className);
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => CanTrigger(hwndUnderCursor, classNameUnderCursor);
    bool CanEnterActionsMode(IntPtr hwnd);

    string? GetSearchScope(IntPtr hwnd);
    bool ExecuteItem(IntPtr hwnd, string path, string searchInput);
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);

    IEnumerable<string> GetListItems(IntPtr hwnd) => Array.Empty<string>();
    void OnSelectionChanged(IntPtr hwnd, string path) { }
    void OnSearchFinished(IntPtr hwnd, bool executed) { }

    // 0보다 크면: 선택 변경이 정리된 뒤 이 많은 밀리초 후에 호스트가 카드 자신의 입력창을
    // 다시 활성화한다. 선택을 반영하면서 포커스를 빼앗는 호스트를 위한 값이다.
    // 기본값인 0은 다시 빼앗지 않는다는 뜻이다.
    int SelectionSyncFocusReclaimDelayMs => 0;
}
```

- **`GetDockBounds`**: 실제 도킹에 사용하는 콘텐츠 영역의 물리 경계를 반환합니다. 호스트는 이 컨테이너 사각형으로 인라인 검색창의 크기와 위치를 정합니다. 해당 경계를 확인할 수 있다면, 어댑자는 관련 없는 바깥 창이 아니라 활성 탐색기 패널이나 대화상자 콘텐츠 영역을 반환해야 합니다.
- **`CanTrigger`**는 *모든* 키 입력을 통과시키는 관문이므로, 주어진 클래스명만으로 답해야 합니다. 후크는 그곳에서 UI Automation 왕복을 감당할 수 없습니다.
- **`GetListItems`**: 선택 미러링에 쓰는, 현재 표시된 행들의 이름입니다. 아무것도 반환하지 않아도 괜찮습니다. 지원하는 일부 관리자는 행 이름을 빈 문자열로 보고하며, 그래서 호스트는 이름만으로 행을 식별하지 않습니다.
- **`CanEnterActionsMode`**: `false`면 이 호스트에서 액션 메뉴가 완전히 사라집니다. 빈 패널을 여는 대신 우클릭과 `Ctrl+O`와 `→`가 모두 물러납니다.
- **`OnSearchFinished(hwnd, executed)`**: 카드가 닫힐 때, 결과가 실제로 실행되었는지 여부와 함께 호출됩니다. 자체 UI(정보 툴팁, 이름 변경 입력)를 숨겨야 했던 호스트는 이때 그것을 되돌려야 합니다.

## 5. 퀵 내비게이션 제공자 `IQuickNavigationProvider`

[**퀵 내비게이션 메뉴**](../../user-guide/hotkeys)에 동적 그룹과 항목을 제공합니다:

```csharp
public enum MouseTriggerType { DoubleClick, MiddleClick }

public interface IQuickNavigationProvider : IPluginComponent
{
    string GroupName { get; }                                   // 루트 그룹 헤더 텍스트
    string IPluginComponent.Name => GroupName;                   // 작성하는 것이 아니라 매핑된다

    Action<ISearchResult>? HeaderAction => null;                 // 헤더 행의 액션 버튼 (예: "+" 버튼)
    string? HeaderActionTooltip => null;                         // 그 버튼의 툴팁

    bool CanProvide(ISearchResult result);
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);

    // 기본값 없음: 구현이 필수다. 메뉴가 닫힐 때, 메뉴가 할당한 채 남긴 것
    // (캐시한 Shell CDS 스트림, 네이티브 아이콘 핸들)을 정리한다.
    void ClearSession();
}
```

- **`HeaderAction`**: 루트 그룹 헤더에 액션 버튼을 덧붙입니다(예: 북마크 제공자가 "현재 폴더 고정"을 붙이는 자리). 폴더 캐스케이터 플러그인의 "현재 폴더 저장" `+` 버튼이 이 멤버입니다.
- **`DynamicMenuItem.IsHeader`**: 중첩 하위 메뉴에서 `IsHeader = true`인 항목을 반환하면 액션 버튼이 있는 인터랙티브 그룹 헤더로 렌더링됩니다.
- **`MouseTriggerType`**: 메뉴를 열 수 있는 두 가지 전역 제스처를 이름으로 적습니다. 그중 어떤 것이 살아 있을지는 제공자 결정이 아니라 사용자 설정입니다 — [**단축키 → 퀵 내비게이션 마우스 트리거**](../../user-guide/settings/hotkeys-page)를 참조하세요.

## 6. 레지스트리

호스트는 `Lertaro.PluginSdk.Registries`의 정적 레지스트리 네 개로 어댑자를 조회하며, 이것이 곧 플러그인 컴포넌트가 Hook 프로세스에 도달하는 경로입니다:

| 레지스트리 | 멤버 |
| :--- | :--- |
| `ActivePathCollectorRegistry` | `Register(IActivePathCollector)`, `GetCollectors()`, `GetAllCollectors()` |
| `FileDialogAdapterRegistry` | `Register(IFileDialogAdapter)`, `GetMatchingAdapter(hwnd, className, processName)`, `GetAdapters()`, `GetAllAdapters()` |
| `InlineSearchAdapterRegistry` | `Register(IInlineSearchAdapter)`, `GetMatchingAdapter(hwnd, className, processName)`, `GetAdapters()`, `GetAllAdapters()` |
| `OpenedFolderCollectorRegistry` | `GetOpenedFolders()` — 활성화된 모든 수집기가 보고하는 내용을 이어 붙입니다. 중복은 **의도적으로 보존**하고, 예외를 던진 수집기 하나는 건너뛰므로 파일 관리자 하나가 망가져도 전체 스냅샷이 함께 무너지지 않습니다 |

앞의 세 레지스트리에는 호스트가 대입하는 `Func<T, bool> FilterFunc`가 하나씩 있습니다. 호스트가 사용자가 활성화한 컴포넌트로 범위를 좁히므로 `GetCollectors()` / `GetAdapters()`는 필터된 뷰를, `GetAllCollectors()` / `GetAllAdapters()`는 등록된 전부를 반환합니다. 플러그인은 이 델리게이트에 값을 넣지 않습니다. 일치 판정은 등록 순서대로 진행되며 `CanHandle`이 먼저 `true`라고 답한 어댑자가 그 창을 가져갑니다. 그래서 범용 `#32770` 대화상자 어댑자가 특화 어댑자가 이미 다루는 창을 주장하면 안 됩니다. 대화상자 레지스트리는 여기에 더해 거부권 하나를 더 행사합니다. 어댑자가 창을 주장한 뒤에도 차단 목록에 오른 창 제목이면 조회는 다음 어댑자로 넘어가지 않고 `null`을 반환하므로, 그 창은 어떤 어댑자도 담당하지 않게 됩니다.
