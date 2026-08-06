# 핵심 검색 및 동작

## `IPluginComponent`와 `IPlugin`

(플러그인 진입 클래스 자체를 포함해) 모든 플러그인 컴포넌트는 `IPluginComponent`를 상속해야 합니다. 이
인터페이스는 컴포넌트의 이름과 설명을 제공합니다.

```csharp
interface IPluginComponent
{
    string Name => GetType().Name;       // Component display name, defaults to type name
    string Description => string.Empty;  // Component description/tooltip shown in settings UI
}
```

모든 플러그인은 메인 진입점으로서 `IPlugin` 인터페이스(`IPluginComponent`를 상속)를 구현해야 하며, 여기에
더해 필요한 다른 인터페이스를 구현합니다.

```csharp
interface IPlugin : IPluginComponent
{
}
```

## 검색 결과 기여하기

### `ISearchableItemProvider`

인덱스에 편입할 완전하고 캐시 가능한 항목 목록을 반환합니다 — 정적이거나 나열하는 데 시간이 걸리지만
매 키 입력마다 변하지는 않는 콘텐츠(예: 시작 메뉴 바로가기, 북마크 목록)에 적합합니다.

```csharp
interface ISearchableItemProvider : IPluginComponent
{
    bool EnableAlias { get; } // default true
    event Action? ItemsChanged;
    IEnumerable<SearchableItem> GetSearchableItems();
}
```

### `IInstantResultProvider`

매 키 입력마다 실행되어 직접 결과를 반환합니다 — 계산기나 URL 바로가기처럼 쿼리 자체에 즉시 대응하는
콘텐츠에 적합하며, 미리 인덱싱해 두고 싶은 종류가 아닙니다.

```csharp
interface IInstantResultProvider : IPluginComponent
{
    IEnumerable<InstantResultItem> GetInstantResults(string query);
    bool[]? GetHighlightMask(string text, string query); // optional match highlighting
}
```

`GetInstantResults`는 오직 동기 방식으로만 동작합니다 — async/취소 토큰 오버로드는 없습니다. 데이터를
가져오는 데 네트워크 왕복이 필요하다면(텍스트 번역, 검색 엔진 제안 가져오기 등), 즉시 플레이스홀더 항목을
반환하고 `Task.Run`으로 실제 작업을 시작한 뒤, 결과가 도착하면 캐시하고
`SearchRefreshService.RefreshIfMatches`([호스트 서비스](./services) 참고)를 호출하여 호스트가 현재
쿼리가 이제 캐시에 걸리는 모든 검색을 다시 실행하도록 하세요 — 실제 예시는 WebSearch 플러그인의 제안
가져오기 로직(`Plugins/WebSearch/WebSearchInstantProvider.cs`)을 참고하세요.

### `IAliasProvider`

ASCII가 아닌 텍스트에 대해 추가로 검색 가능한 문자열을 생성합니다 — 중국어 파일명을 위한 병음 별칭이
동작하는 방식이 바로 이것입니다([PinyinAlias](../examples#pinyinalias-—-중국어-파일명을-위한-병음-별칭)
참고).

```csharp
interface IAliasProvider
{
    string Name { get; }
    bool CanHandle(string text);
    IReadOnlyList<(char Start, char End)> InputRanges { get; }
    IReadOnlyList<(char Start, char End)> OutputRanges { get; }
    IEnumerable<string> GetAliases(string text);

    int Version { get; } // default 1
    int[]? MapAliasToSourceIndices(string text, string alias); // default null
    void GetAliasesUtf8(string text, AliasByteSink dest); // default: adapts GetAliases
    IEnumerable<string> GetQueryForms(string term); // default: none
}
```

`InputRanges`와 `OutputRanges`에는 기본값이 없습니다 — 모든 제공자가 이를 반드시 선언해야 합니다.
`InputRanges`는 이 제공자가 변환의 *출발점*으로 삼는 문자 범위(예: 병음이라면 CJK 표의문자 블록)이고,
`OutputRanges`는 생성되는 별칭을 이루는 범위(예: 소문자 `a`-`z`)입니다. 호스트는 이 둘을 함께 사용하여,
ASCII 여부를 추측하는 대신 제공자 자신의 입력/출력 알파벳이 섞인 쿼리 항목(예: 후보 `大长今`에 대한
`大cj`)을 후보 자체의 텍스트에 매칭되는 리터럴 구간과 이 제공자의 별칭에 매칭되는 별칭 구문 구간으로
분할합니다.

`Version`, `MapAliasToSourceIndices`, `GetAliasesUtf8`은 모두 기본 구현이 제공되어, 대부분의 제공자는
이를 건드릴 필요가 없습니다.

- **`Version`**: 동일한 입력에 대해 이 제공자의 출력이 달라질 수 있을 때(알고리즘 수정, 새 규칙, 갱신된
  데이터 테이블) 값을 올리세요. 인덱스는 이를 사용하여 이 제공자가 이전에 생성한 별칭이 오래되어 다시
  생성해야 함을 감지합니다.
- **`MapAliasToSourceIndices`**: 별칭에 대해 발견된 매치(예: 어떤 병음 글자가 매치되었는지)를 원본
  텍스트에 다시 매핑하여 하이라이트할 수 있게 해줍니다 — 그렇지 않으면 쿼리가 변환되지 않은 원본 텍스트
  안에 그대로 나타나지 않기 때문에 아무것도 하이라이트되지 않습니다. 이 별칭이 이 제공자가 이 텍스트에
  대해 만든 것이 아니거나 매핑이 지원되지 않는다면 `null`(기본값)을 반환하세요 — 호스트는 이를 오류가
  아니라 "이 제공자를 통해서는 하이라이트할 수 없음"으로 처리합니다.
- **`GetAliasesUtf8`**: 호스트의 대량 인덱싱 경로에서 사용되는 바이트 네이티브 변형으로, 이 경로에서
  별칭은 결국 UTF-8 바이트로 저장됩니다. 기본 구현은 `GetAliases`를 그대로 사용하므로 기존 제공자는
  변경 없이 동작합니다. 여러분의 제공자가 매우 많은 양의 별칭을 생성하고 그 문자열 생성 비용이 실제로
  체감될 때만 이를 재정의하여 문자열 생성 자체를 건너뛰도록 하세요.
- **`GetQueryForms`**: `GetAliases`의 쿼리 쪽 대응물입니다 — 사용자가 입력한 쿼리 항목 하나를 이
  제공자 자신의 별칭이 사용하는 것과 같은 구분자 구조로 다시 작성합니다. 그렇게 하면 사용자가 그저
  연속된 문자로 입력한 쿼리 항목도 호스트가 이해하지 못하는 구조(예를 들어 병음의 음절 경계 —
  이것이 바로 쿼리가 서로 관련 없는 두 음절에 걸쳐 매칭되는 것을 막아줍니다)를 그대로 유지할 수
  있습니다. 아무것도 반환하지 않는 것(기본값)은 "이 항목은 내 알파벳에 전혀 속하지 않는다"는
  뜻이며, 이것이 바로 이 제공자가 표현할 수 없는 쿼리가 원래 매칭되어서는 안 될 별칭에 매칭되는
  것을 막아줍니다. 쿼리마다 항목당 한 번만 호출되고 후보마다 호출되지는 않으므로 여기서 실질적인
  작업을 해도 괜찮습니다 — 다만 반환하는 형태가 많아질수록 각각이 모든 후보와 대조되는 추가
  대안이 되므로, 많이 반환할수록 비용이 커집니다.

### `IQueryTokenProvider`

쿼리의 마지막 토큰(예: `report :size`, `report ::"hello world"`, `report ::hello\ world`)을 가져가서 이미 매칭된 결과 목록을 변형합니다 — 정렬, 필터링, 또는 일반 검색 위에 다른 방식으로 구성하는 방식입니다. 큰따옴표(`"..."`), 작은따옴표(`'...'`) 또는 이스케이프 공백(`\ `)을 사용하여 토큰 내에 공백을 포함할 수 있습니다.

```csharp
interface IQueryTokenProvider : IPluginComponent
{
    bool CanHandle(string token);
    Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results);
}
```

## 결과에 대한 동작

### `IActionProvider`

정적 동작과 동적 동작을 모두 노출하기 위해 플러그인이 구현하는 컨테이너입니다.

```csharp
interface IActionProvider
{
    IEnumerable<ISearchResultAction> GetActions();
    IEnumerable<IDynamicActionProvider> GetDynamicActionProviders();
}
```

### `ISearchResultAction`

Actions 메뉴나 퀵 창의 동작 단축키에 표시되는 하나의 정적 동작(예: "경로 복사")입니다.

```csharp
interface ISearchResultAction : IPluginComponent
{
    string GroupName { get; }
    string DisplayName { get; }
    string? Hotkey { get; }              // optional default hotkey
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    ImageSource Icon { get; }
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool CanExecute(IReadOnlyList<ISearchResult> selection);
    void Execute(IReadOnlyList<ISearchResult> selection, IPluginSearchWindow window);
}
```

### `IDynamicActionProvider`

고정된 목록을 반환하는 대신 런타임에 메뉴 항목을 구성합니다 — 실제 Windows 셸 마우스 우클릭 메뉴(중첩된
계단식 하위 메뉴 포함)가 Lertaro의 Actions 메뉴 안에 노출되는 방식이 바로 이것입니다. 자세한 내용은
[ShellMenuActionProvider](../examples#coreextensions-—-동작과-셸-컨텍스트-메뉴)를
참고하세요.

```csharp
interface IDynamicActionProvider
{
    string GroupName { get; }
    int? Priority { get; }
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    void Init();
    bool CanProvide(IReadOnlyList<ISearchResult> selection);
    IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> selection, IntPtr hMenu);
    IEnumerable<(string Hotkey, Action Execute)> GetHotkeyActions(IReadOnlyList<ISearchResult> selection);
    void ExecuteCommand(IReadOnlyList<ISearchResult> selection, uint commandId, IntPtr ownerHwnd);
    void ClearSession();
}
```

`Init()`은 호스트가 프로세스당 최대 한 번, 액션 메뉴가 처음 열릴 때 호출합니다 — 실제로 어떤 선택에 대해
`CanProvide`/`GetMenuItems`가 호출되기 전입니다. 호스트가 "최대 한 번"이라는 부분을 보장하므로, 구현체
쪽에서 반복 호출에 대비할 필요는 없습니다. 이를 활용해 시간이 걸리는 일회성 초기화(예: 네이티브 워커
스레드 예열) 작업을 처리하세요 — 이렇게 하면 뒤이어 리드 타임 없이 곧바로 뒤따르는 여러분 자신의
`CanProvide`/`GetMenuItems` 호출과 경쟁하는 대신 진짜로 앞서 시작할 수 있습니다. 블로킹해서는 안 되므로,
실제 작업은 반드시 백그라운드 스레드에서 수행하세요. 기본 구현은 아무 동작도 하지 않습니다.

`Priority`는 Actions 메뉴의 동적(제공자별) 그룹들 사이에서 이 제공자 자신의 섹션 위치를 제어합니다 — 값이
낮을수록 먼저 나타나며, 기본값은 `0`입니다. 다만 이는 어디까지나 폴백일 뿐입니다 — 사용자는
[설정 → 일반 → 전체 검색 창](../../user-guide/settings/general#전체-검색-창) 아래에서 이
섹션들(여러분의 것도 포함해서)을 직접 드래그로 재정렬할 수 있으며, 사용자가 명시적으로 순서를 정한
섹션은 `Priority`가 무엇을 반환하든 그 위치를 유지합니다.

## 지원 모델

- **`SearchableItem`** / **`InstantResultItem`** — Title, Description, IconData, IconColor,
  ActionType(`"Copy"` / `"Execute"` / `"None"`), ActionArgument, TabCompletion, `HBitmapIcon`(설정된
  경우 IconData보다 우선하는, 미리 로드된 GDI HBITMAP — 호스트가 소유권을 가져가서 다 쓰고 나면 직접
  DeleteObject를 호출하므로, 이후에 직접 핸들을 재사용하거나 해제하지 마세요. 실제 예시는 Window
  Switcher 플러그인 자체의 창 썸네일 캡처를 참고하세요)를 공유합니다. `SearchableItem`은 여기에 더해
  `OnExecute`(직접 호출 델리게이트)와 `ResultKind`(예: `"Application"`/`"File"` 재정의)를 갖습니다.
- **`DynamicMenuItem`** — Text, CommandId, IsSeparator, HasSubMenu, SubMenuHandle, IsDisabled,
  HBitmapItem, OnExecute, ShortcutHint, IsHeader. `IsHeader`는 항목을 일반 행 대신 클릭할 수 없는
  섹션 헤더 행(예: Quick Navigation 하위 메뉴 자체의 그룹 이름)으로 렌더링합니다 — Text가 헤더 레이블이
  되며, `OnExecute`도 함께 설정되어 있으면 헤더의 끝단에 작은 버튼이 나타나 이를 호출합니다.
  `IsHeader`가 true일 때는 그 밖의 모든 필드는 무시됩니다. 이는
  [`IQuickNavigationProvider.HeaderAction`](./system-adapters#iquicknavigationprovider)의 하위
  메뉴 깊이 버전이라고 볼 수 있으며, `HeaderAction`은 루트 레벨만을 다룹니다.
- **`SearchWindowType`** 열거형 — `Main`, `Quick`, `Inline`. 동작이나 제공자가 세 창
  ([사용자 매뉴얼](../../user-guide/getting-started#세-가지-창) 참고) 중 어느 것에 표시되고
  있는지에 따라 다르게 동작하도록 해줍니다.
