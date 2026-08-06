# 시스템 및 대화상자 어댑터

이 인터페이스들은 플러그인이 Lertaro를 자체 검색 창뿐 아니라 *다른* 창 — 파일 탐색기, 네이티브 파일
선택 대화상자, 타사 파일 관리자 — 와 통합할 수 있게 해줍니다.

## `IActivePathCollector`

현재 포그라운드 창이 무엇이든 그로부터 "현재 디렉터리"를 추출하여, Lertaro가 검색 범위를 어디로
한정해야 할지(또는 상대적인 동작을 어디를 기준으로 해석해야 할지) 알 수 있게 합니다.

```csharp
interface IActivePathCollector
{
    string Name { get; }
    string TargetName { get; }   // localized name of the app/manager this targets
    bool CanHandle(string className);
    string? TryGetPath(
        IntPtr activeHwnd, string activeClassName,
        IntPtr windowHwnd, string windowClassName,
        string processName);
}
```

활성(포커스를 가진) 요소와 그 요소를 담고 있는 창은 별도로 전달됩니다. 많은 파일 관리자가 실제 경로를
최상위 창 자체가 아니라 자식 컨트롤(주소 표시줄, 트리 뷰 선택 항목 등)에 두기 때문입니다.

## `IFileDialogAdapter`

네이티브로 렌더링되는 Windows 열기/저장 파일 대화상자를 읽고 조작하여, Lertaro를 그 안에 내장할 수
있게 하고(아래 [`IInlineSearchAdapter`](#iinlinesearchadapter) 참고) 서로 동기화된 상태를 유지합니다.

```csharp
interface IFileDialogAdapter
{
    string Name { get; }
    bool CanHandle(IntPtr hwnd, string className, string processName);
    string? GetCurrentPath(IntPtr hwnd);
    bool NavigateTo(IntPtr hwnd, string targetPath);
    bool TargetIsFolderOnly { get; } // default: false
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor); // default: true
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    bool RestoreFocus(IntPtr hwnd);
}
```

`TargetIsFolderOnly`는 대상 필드가 오직 폴더만 담을 수 있는 대화상자(예: 압축 도구의 "여기에 압축 풀기"
대상)에 대해 `true`입니다 — 열기/저장 대화상자의 파일명 상자와 달리 특정 파일은 절대 담지 못합니다.
호스트는 이를 사용해 선택된 검색 결과가 파일일 경우 `NavigateTo`에 도달하기 전에 그 파일이 담긴 폴더로
먼저 해석해야 할지를 판단합니다 — `NavigateTo` 자체에 그 판단을 맡기지 않는 이유는, 그 호출이 상승된
권한의 Hook 프로세스에서 실행되며 그곳에서는 사용자가 비상승 권한으로 매핑한 드라이브에 대해
`File.Exists`/`Directory.Exists`를 신뢰할 수 없기 때문입니다. 실제 파일명 상자가 있는 대화상자라면
기본값인 `false`로 두세요.

## `IInlineSearchAdapter`

대상 파일 대화상자나 파일 탐색기 창(사용자 매뉴얼의 "인라인 창")에 Lertaro 검색창을 직접 내장하며,
선택 상태를 양방향으로 동기화된 상태로 유지합니다.

```csharp
interface IInlineSearchAdapter
{
    string Name { get; }
    bool IsFileExplorer { get; }   // default false
    bool CanHandle(IntPtr hwnd, string className, string processName);
    bool CanTrigger(IntPtr focusedHwnd, string className);
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor); // default: delegates to CanTrigger
    bool CanEnterActionsMode(IntPtr hwnd);
    string? GetSearchScope(IntPtr hwnd);
    bool ExecuteItem(IntPtr hwnd, string path, string searchInput);
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    IEnumerable<string> GetListItems(IntPtr hwnd);        // optional
    void OnSelectionChanged(IntPtr hwnd, string path);    // optional
    void OnSearchFinished(IntPtr hwnd, bool executed);    // optional
}
```

`AdapterRect`(`IFileDialogAdapter`와 공유)는 단순한 `{ Left, Top, Right, Bottom }` `int` 사각형입니다.

## `IQuickNavigationProvider`

Quick Navigation 팝업의 콘텐츠(보통 계단식 메뉴)를 제공합니다 —
[단축키 → 빠른 탐색](../../user-guide/hotkeys#빠른-탐색-마우스)을 참고하세요. 특정 클릭에 대해
팝업이 실제로 열리는지 여부는 이 인터페이스가 아니라 호스트가 결정합니다 — 이미
`IInlineSearchAdapter`/`IFileDialogAdapter`가 인식하는 창이라면 자동으로 트리거되므로, 이 인터페이스는
순수하게 콘텐츠 공급원 역할만 합니다.

```csharp
interface IQuickNavigationProvider
{
    string GroupName { get; }
    Action<ISearchResult>? HeaderAction => null;
    string? HeaderActionTooltip => null;
    bool CanProvide(ISearchResult result);
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);
    void ClearSession();
}
```

`GroupName`은 이 제공자 자체의 루트 레벨 항목들 위에 표시되는 섹션 헤더에 레이블을 붙입니다. 이렇게
하면 둘 이상의 빠른 탐색 제공자를 활성화한 사용자도 어느 항목이 어디서 왔는지 구분할 수 있습니다 —
Actions 메뉴에서 `IDynamicActionProvider.GroupName`이 하는 역할과 동일합니다.

`HeaderAction`(선택 사항, 기본값 `null`)은 같은 루트 레벨 그룹 헤더에 작은 버튼을 추가합니다 — 예를 들어
북마크 스타일의 제공자라면 "현재 폴더 추가하기"에 이를 사용할 수 있습니다. 이는 `GetMenuItems` 자체가
루트 레벨에서 받는 것과 동일한 `ISearchResult`로 호출됩니다. `HeaderActionTooltip`은 그 버튼의 툴팁을
설정하며, `HeaderAction`이 null이면 무시됩니다. 중첩된 하위 메뉴(루트보다 아래의 어떤 깊이든)는 자체
호스트 렌더링 헤더가 없으므로, `HeaderAction`의 효과는 루트에서 그칩니다 — 하위 메뉴에도 동일한 "+"
버튼을 원하는 제공자는 대신 해당 하위 메뉴의 첫 항목으로 `IsHeader = true`인 `DynamicMenuItem`(아래
참고)을 반환하고, 그 항목 자체의 `OnExecute`가 같은 역할을 하도록 합니다.

`DynamicMenuItem`은
[`IDynamicActionProvider`](./core-search-actions#idynamicactionprovider)가 사용하는 것과 동일한
모델이며, 하위 메뉴 레벨의 헤더 행을 위한 `IsHeader` 플래그도 함께 갖습니다.
