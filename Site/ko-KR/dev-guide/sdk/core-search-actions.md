# 검색 코어 및 액션

이 장에서는 `Lertaro.PluginSdk`에서 검색 데이터 소스, 실시간 연산 결과, 비 ASCII 별칭 엔진, 쿼리 접미사 토큰 핸들러 및 컨텍스트 액션 메뉴를 제공하기 위한 핵심 인터페이스와 데이터 구조를 다룹니다.

## 1. 기본 컴포넌트 규격 `IPluginComponent` 및 `IPlugin`

모든 플러그인 컴포넌트는 `IPluginComponent`를 상속하여 호스트에 메타데이터를 제공합니다.

```csharp
namespace Lertaro.PluginSdk;

public interface IPluginComponent
{
    string Name => GetType().Name;      // 컴포넌트 표시 이름 (기본값: 클래스명)
    string Description => string.Empty; // 설정 창에서 툴팁으로 표시될 설명 문구
}

public interface IPlugin : IPluginComponent
{
    // 플러그인 어셈블리 메인 진입점 식별자
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
}
```

> [!TIP]
> `GetInstantResults`는 타이핑 반응성을 위해 동기식으로 호출됩니다. 비동기 네트워크 요청(번역, 검색 제안 등)이 필요한 경우 플레이스홀더를 즉시 반환하고 `Task.Run`으로 백그라운드에서 조회 후 `SearchRefreshService.RefreshIfMatches`를 호출하여 호스트 검색 결과를 갱신하세요.

### 비 ASCII 별칭 변환 엔진 `IAliasProvider`

중국어 파일명 등 비 ASCII 텍스트에 대한 인덱싱용 별칭을 생성하여 병음 혼합 검색을 지원합니다.

```csharp
public interface IAliasProvider
{
    string Name { get; }
    bool CanHandle(string text);
    IReadOnlyList<(char Start, char End)> InputRanges { get; }  // 입력 문자 범위 (예: CJK 한자)
    IReadOnlyList<(char Start, char End)> OutputRanges { get; } // 출력 문자 범위 (예: a-z)
    IEnumerable<string> GetAliases(string text);

    int Version => 1;                                           // 규칙 변경 시 증가시켜 재인덱싱 유도
    int[]? MapAliasToSourceIndices(string text, string alias) => null; // 하이라이트 역매핑
    void GetAliasesUtf8(string text, AliasByteSink dest);       // 제로 할당 바이트 빌더
    IEnumerable<string> GetQueryForms(string term);             // 쿼리 측 음절 분할 전개
}
```

### 쿼리 접미사 토큰 핸들러 `IQueryTokenProvider`

검색어 끝에 붙는 토큰(예: `report :size`, `doc :@today`, `image ::"hello world"`)을 감지하여 결과 목록에 필터링이나 정렬을 적용합니다.

```csharp
public interface IQueryTokenProvider : IPluginComponent
{
    bool CanHandle(string token);
    Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results);
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
public interface ISearchResultAction : IPluginComponent
{
    string GroupName { get; }           // 액션 그룹 이름
    string DisplayName { get; }         // 표시 텍스트
    string? Hotkey { get; }             // 기본 단축키 (예: "Ctrl+Shift+C")
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    ImageSource Icon { get; }           // 아이콘
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool CanExecute(IReadOnlyList<ISearchResult> selection);
    void Execute(IReadOnlyList<ISearchResult> selection, IPluginSearchWindow window);
}
```

### 동적 메뉴 빌더 `IDynamicActionProvider`

런타임에 동적으로 메뉴를 생성합니다(Windows Shell 우클릭 메뉴 통합 등).

```csharp
public interface IDynamicActionProvider
{
    string GroupName { get; }
    int? Priority => 0;                 // 메뉴 표시 우선순위
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    void Init() { }                     // 프로세스 수명 중 1회만 호출되는 웜업 초기화
    bool CanProvide(IReadOnlyList<ISearchResult> selection);
    IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> selection, IntPtr hMenu);
    IEnumerable<(string Hotkey, Action Execute)> GetHotkeyActions(IReadOnlyList<ISearchResult> selection);
    void ExecuteCommand(IReadOnlyList<ISearchResult> selection, uint commandId, IntPtr ownerHwnd);
    void ClearSession() { }
}
```

## 4. 보조 데이터 구조

- **`SearchableItem` / `InstantResultItem`**: `Title`, `Description`, `IconData`, `IconColor`, `ActionType`, `ActionArgument`, `TabCompletion`, `HBitmapIcon`(호스트 자동 해제), `OnExecute` 등을 포함.
- **`DynamicMenuItem`**: `Text`, `CommandId`, `IsSeparator`, `HasSubMenu`, `SubMenuHandle`, `IsDisabled`, `IsActionable`, `IsContinuation`, `OnExecute`, `IsHeader`, `ShortcutHint`를 포함합니다. 하위 메뉴만 여는 순수 범주 노드는 `IsActionable = false`로 설정하고, 실제 폴더 항목은 기본값 `true`를 유지할 수 있습니다. `IsContinuation = true`는 페이지로 나뉜 하위 메뉴의 연속 커서를 나타내며 호스트가 자동으로 다음 페이지를 로드하므로 “더 불러오기” 행으로 표시되지 않습니다. `IsHeader`는 선택적 작업 버튼이 있는 그룹 헤더로 렌더링합니다.
- **`SearchWindowType`**: `Main`(메인 창), `Quick`(퀵 검색창), `Inline`(인라인 파일 대화상자) 열거형.
