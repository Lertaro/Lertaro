# 검색 코어 및 액션

이 장에서는 `Lertaro.PluginSdk`에서 검색 데이터 소스, 실시간 연산 결과, 비 ASCII 별칭 엔진, 쿼리 접미사 토큰 핸들러 및 컨텍스트 액션 메뉴를 제공하기 위한 핵심 인터페이스와 데이터 구조를 다룹니다.

## 1. 기본 컴포넌트 규격 `IPluginComponent` 및 `IPlugin`

모든 플러그인 컴포넌트는 `IPluginComponent`를 상속하여 호스트에 메타데이터를 제공합니다.

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IPluginComponent
{
    string Name => GetType().Name;      // 컴포넌트 표시 이름 (기본값: 클래스명)
    string Description => string.Empty; // 설정 창에서 툴팁으로 표시될 설명 문구
}

public interface IPlugin : IPluginComponent
{
    // 플러그인 어셈블리 메인 진입점. 두 웹사이트 멤버는 모두 선택 사항이고,
    // WebsiteUrl이 null이면 설정 카드에는 링크가 아예 표시되지 않는다.
    string? WebsiteUrl => null;
    string? WebsiteLabel => null;
}
```

## 2. 검색 결과 제공

### 정적 캐시 가능 항목 제공자 `ISearchableItemProvider`

키 입력마다 변경되지 않고 사전에 인덱싱하기에 적합한 데이터 소스(시작 메뉴 바로가기, 북마크, 제어판 항목 등)에 사용합니다.

```csharp
public interface ISearchableItemProvider : IPluginComponent
{
    bool EnableAlias => true;           // 병음 등의 별칭 변환 허용 여부
    event Action? ItemsChanged;         // 데이터 변경 시 재인덱싱을 요청하는 이벤트
    IEnumerable<SearchableItem> GetSearchableItems();
}
```

### 동적 실시간 연산 제공자 `IInstantResultProvider`

키 입력마다 즉시 실행되며 검색어 자체로부터 결과를 도출하는 기능(계산기, 진법 변환, URL 점프 등)에 적합합니다.

```csharp
public interface IInstantResultProvider : IPluginComponent
{
    IEnumerable<InstantResultItem> GetInstantResults(string query);
    bool[]? GetHighlightMask(string text, string query) => null; // 커스텀 하이라이트 마스크

    // 이 제공자를 호출하는 단어어들. 호스트 자신의 제거 단계가 이 값을 읽으므로
    // 호스트의 제거와 플러그인의 일치가 서로 어긋날 수 없다. 6장 "트리거 단어" 참조.
    IReadOnlyList<string> QueryTriggerKeywords => [];
}
```

> [!TIP]
> `GetInstantResults`는 타이핑 반응성을 위해 동기식으로 호출됩니다. 비동기 네트워크 요청(번역, 검색 제안 등)이 필요한 경우 플레이스홀더를 즉시 반환하고 `Task.Run`으로 백그라운드에서 조회 후 `SearchRefreshService.RefreshIfMatches`를 호출하여 호스트 검색 결과를 갱신하세요.

### 비 ASCII 별칭 변환 엔진 `IAliasProvider`

중국어 파일명 등 비 ASCII 텍스트에 대한 인덱싱용 별칭을 생성하여 병음 혼합 검색을 지원합니다.

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IAliasProvider : IPluginComponent   // 이름은 IPluginComponent에서 나온다
{
    bool CanHandle(string text);
    IReadOnlyList<(char Start, char End)> InputRanges { get; }  // 입력 문자 범위 (예: CJK 한자)
    IReadOnlyList<(char Start, char End)> OutputRanges { get; } // 출력 문자 범위 (예: a-z)

    // 다음절 엔진(병음)에서 설정하는 값: 생성된 별칭에서 음절을 잇는 문자다.
    // 기본값 '\0'은 엔진이 구분자를 전혀 내지 않는다는 뜻이다.
    char SyllableSeparator => '\0';

    IEnumerable<string> GetAliases(string text);

    int Version => 1;                                           // 규칙 변경 시 증가시켜 재인덱싱 유도
    IEnumerable<string> GetQueryForms(string term) => Array.Empty<string>(); // 쿼리 측 음절 분할 전개
    int[]? MapAliasToSourceIndices(string text, string alias) => null;       // 하이라이트 역매핑

    // 인덱서 핫 경로를 위한 제로 할당 UTF-8 빌더. 기본 구현은 GetAliases()를 싱크로 넘기면서
    // 실제로 대문자를 포함한 별칭만 소문자로 내리므로, 엔진은 문자열보다 싸게 바이트를
    // 생성할 수 있을 때만 이 메서드를 재정의한다.
    void GetAliasesUtf8(string text, AliasByteSink dest);
}
```

### 쿼리 접미사 토큰 핸들러 `IQueryTokenProvider`

검색어 끝에 붙는 토큰(예: `report :size`, `doc :@today`, `image ::"hello world"`)을 감지하여 결과 목록에 필터링이나 정렬을 적용합니다.

```csharp
public interface IQueryTokenProvider : IPluginComponent
{
    bool CanHandle(string token);
    Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results);

    // 이 토큰이 검색어에서 소비된 뒤 결과 행에서 계속 강조할 텍스트.
    // 기본값 null은 호스트 자신의 강조를 건드리지 않는다.
    string? GetHighlightText(string token) => null;
}
```

## 3. 결과 컨텍스트 액션

### 액션 제공자 컨테이너 `IActionProvider`

```csharp
public interface IActionProvider
{
    IEnumerable<ISearchResultAction> GetActions();
    IEnumerable<IDynamicActionProvider> GetDynamicActionProviders();
}
```

### 정적 액션 계약 `ISearchResultAction`

`Ctrl+O` 메뉴나 단축키에 등록되는 독립적인 정적 동작(경로 복사, 관리자 권한 실행 등)을 정의합니다.

```csharp
namespace Lertaro.PluginSdk.Abstractions;

public interface ISearchResultAction : Plugins.IPluginComponent
{
    string GroupName { get; }           // 액션 메뉴의 그룹 헤더
    string DisplayName { get; }         // 액션 제목
    // 액션은 표시 이름으로 지목되므로 Name은 직접 작성하는 대신 매핑한다:
    string Plugins.IPluginComponent.Name => DisplayName;

    // nullable이 아니며 기본값이 있다. 빈 문자열은 "단축키 없음"을 뜻하며, 파괴적인 파일
    // 액션들이 탐색기의 조합키를 되찾기 전까지 바인딩되지 않은 채로 있던 방식이 이것이다.
    string Hotkey => string.Empty;
    IReadOnlyList<string> Keywords => Array.Empty<string>();
    IReadOnlyList<string> Parameters => Array.Empty<string>();

    // 액션이 어디에 표시되는가. 기본값: 검색에는 항상 표시되고 메뉴에는 키워드를
    // 주장하지 않을 때만 표시된다(키워드가 있으면 대신 행으로 나타난다).
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => true;
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => Keywords.Count == 0;

    ImageSource? Icon { get; }          // 액션 아이콘; null이면 그룹 기본 아이콘을 그린다
    bool CanExecute(IReadOnlyList<ISearchResult> results);
    void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view);
}
```

### 동적 메뉴 빌더 `IDynamicActionProvider`

런타임에 동적으로 메뉴를 생성합니다(Windows Shell 우클릭 메뉴 통합 등).

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IDynamicActionProvider : IPluginComponent
{
    string GroupName { get; }
    string IPluginComponent.Name => GroupName;    // ISearchResultAction과 같은 매핑

    int Priority => 0;                            // 메뉴 정렬 가중치, nullable이 아니다
    IReadOnlyList<string> Keywords => Array.Empty<string>();
    IReadOnlyList<string> Parameters => Array.Empty<string>();

    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => true;
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => Keywords.Count == 0;

    void Init() { }                               // 최초 메뉴 이후에 호출되는 1회성 웜업
    bool CanProvide(IReadOnlyList<ISearchResult> results);

    // 즉시 결과(창 제목, 프로세스 행) 위에도 메뉴를 제공하겠는지 선택한다. 기본값 false —
    // 대부분 제공자는 그런 행이 갖고 있지 않은 파일 경로 기준으로 동작하기 때문이다.
    bool CanProvideForInstantResults => false;

    IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> results, IntPtr hMenu);
    IEnumerable<(string Hotkey, Action Execute)> GetHotkeyActions(IReadOnlyList<ISearchResult> results)
        => Array.Empty<(string, Action)>();
    void ExecuteCommand(IReadOnlyList<ISearchResult> results, uint commandId, IntPtr ownerHwnd);

    // 기본 구현 없음: 구현이 필수다. 메뉴가 해제될 때 호출되므로 네이티브 핸들이나
    // 캐시한 Shell CDS 스트림을 가진 제공자는 여기서 반환할 수 있다.
    void ClearSession();
}
```

## 4. 보조 데이터 구조

- **`SearchableItem`**: `Title`, `Description`, `IconData`, `IconColor`, `ActionType`(`"Copy"` / `"Execute"` / `"None"`), `ActionArgument`, `TabCompletion`, `HBitmapIcon`(호스트 자동 해제), `ResultKind`(호스트의 필터와 열이 기준으로 삼을 수 있는, 플러그인이 선택한 태그), 실행 콜백 두 개(`OnExecute`(`Action`)는 fire-and-forget용, 작업이 성공 여부를 알려야 할 때는 `OnExecuteFunc`(`Func<bool>`) — 호스트는 그 답변으로 예를 들어 창을 닫을지 결정함)를 포함합니다. `InstantResultItem`은 표시 및 콜백 멤버를 똑같이 갖지만 **`ResultKind`는 없습니다**. `ResultKind`는 검색 가능 항목 모델에만 있는 멤버입니다.
- **`DynamicMenuItem`**: `Text`, `CommandId`, `IsSeparator`, `HasSubMenu`, `SubMenuHandle`, `IsDisabled`, `OnExecute`, `IsActionable`(기본값 `true`; `false`는 하위 메뉴를 여는 일 외에는 동작하지 않는 행을 표시), `HBitmapItem`(미러링 중인 Shell 메뉴의 네이티브 아이콘 핸들), `ShortcutHint`(메모닉 키가 일치시키는 문자), `IsContinuation`(페이지 나누기 커서 — 이번 묶음이 호스트가 아직 채우고 있는 메뉴의 연속이며, 이 값이 설정되어 있는 동안 호스트는 계속 요청함), `IsHeader`(선택적 액션 버튼이 있는 그룹 헤더로 렌더링)를 포함합니다.
- **`SearchWindowType`**: `Main`(메인 창), `Quick`(퀵 검색창), `Inline`(인라인 파일 대화상자) 열거형.

## 5. 명명된 검색 범위 `ISearchScopeProvider`

**범위**는 키워드 접두사와 디렉터리 집합의 조합입니다: `tf report`를 입력하면 호스트는 `report`로 보통의 인덱스 검색을 되되, 그 폴더들로 제한해 실행합니다. 기존 인덱스 위에 걸치는 2단계 필터이지, 두 번째 검색 엔진이 아닙니다.

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface ISearchScopeProvider : IPluginComponent
{
    // 키 입력 디스패치마다 참조하므로 캐시한 목록을 반환하고, 설정이 바뀔 때만 다시 만든다.
    // 키워드가 비었거나 폴더가 없는 범위는 호스트가 무시한다.
    IReadOnlyList<SearchScope> GetSearchScopes();
}

public sealed class SearchScope
{
    public string Keyword { get; init; } = string.Empty;            // 대소문자 무관 첫 토큰, 예: "tf"
    public IReadOnlyList<string> Folders { get; init; } = Array.Empty<string>();
    public string FilterPattern { get; init; } = "*";               // FILE 이름에 대한 ';' 구분 Win32 와일드카드
}
```

`ISearchableItemProvider`와 달리 범위 제공자는 파일을 열거하거나 실체화하지 않으므로, 설정한 폴더가 아무리 커도 메모리와 키 입력당 비용은 일정하게 유지됩니다. 어느 호스트 인덱스도 커버하지 않는 폴더는 실시간으로 훑지 않고 경고와 함께 건너뛰며 — 이는 인덱서 헬퍼가 따르는 규칙과 같은데, 로컬 드라이브·네트워크·폴더 인덱스로 그 폴더를 먼저 커버해야 한다는 것입니다. 디렉터리는 `FilterPattern`을 항상 통과합니다.

저장소 안 구현체는 File Filters 플러그인입니다.

## 6. 트리거 단어 `TriggerWord`

사용자가 앞단 단어를 입력해 호출하는 기능 — 즉시 제공자의 `QueryTriggerKeywords`, 액션의 `Keywords`, 범위의 `Keyword`, 파일 필터의 트리거 — 은 그 단어를 모두 `Lertaro.PluginSdk.Services.TriggerWord`로 해석합니다. 그래서 호스트의 단어 제거와 플러그인의 일치 판정이 서로 어긋날 수 없습니다.

| 도우미 | 판정 내용 |
| :--- | :--- |
| `string Normalize(string? configured)` | 설정해 둔 단어를 모든 비교가 기대하는 형태로 만듭니다: 앞뒤 공백 제거, `null`이나 공백만 있으면 빈 문자열. 읽을 때 정규화하세요 — 호스트는 제거하는 단어를 trim하므로, trim하지 않은 값과 비교하면 아무것도 인식되지 않는데 호스트는 여전히 파일 검색에서 그 단어를 지웁니다. |
| `bool TryMatch(string query, string? word, out string argument)` | 첫 토큰이 단어와 **동일**해야 합니다(대소문자 무관). `argument`는 남은 텍스트를 trim한 값이며 검색어가 단어뿐이면 빈 문자열이 되고, 그때도 일치합니다. 그 단어로 시작하는 더 긴 단어는 일치하지 않습니다(`csreport`는 `cs`가 아님). |
| `bool TryMatchInvoked(...)` | 위와 같지만 단어 뒤에 실제로 무언가 입력된 뒤에만 단어가 인정됩니다. 맨 단어만으로도 아무도 요청하지 않은 행이 화면에 뜨는 경우에 쓰세요: `cs`만 있으면 파일 검색으로 남고, `cs `가 제공자입니다. |
| `bool TryMatchAny(string query, IReadOnlyList<string> words, out string matchedWord, out string argument)` | 목록 순서대로 처음 일치하는 단어가 이기며, **어느** 단어가 일치했는지도 보고합니다 — 제공자마다 여러 키워드를 두는 웹 검색 엔진은 재파싱에 그 값이 필요합니다. |
| `bool IsTypedPrefixOf(string query, string? word)` | 검색어가 아직 완성되지 않은 단어 입력 중일 때 참입니다(`mkdir`로 가는 중인 `m`). 단어 전체가 갖춰지기 전에 트리거를 제시할 수 있는 유일한 분기이며, 구분자가 입력되는 순간 false가 됩니다. |

`IInstantResultProvider.QueryTriggerKeywords`(기본값은 비어 있음)는 호스트가 자신의 제거 단계에서 읽는 값이고, `PluginConfigField.IsTriggerWord`는 그런 단어를 담는 `Text` 설정에 표시되어, 다른 기능이 이미 같은 단어로 응답하고 있을 때 설정 페이지가 저장은 막지 않은 채 경고할 수 있게 합니다. 그런 표시가 없다면 한 단어를 두 기능이 쓰는 문제는 조용히 지나갑니다: 파일 검색은 먼저 등록된 쪽을 따르고 다른 쪽의 행은 단순히 사라질 뿐, 어느 쪽 이름을 바꿔야 하는지 알려 주는 것이 없습니다.
