# 시스템 및 대화상자 어댑터

이 장에서는 Windows 파일 탐색기, 네이티브 파일 대화상자, 서드파티 파일 관리자와 연동하기 위한 `Lertaro.PluginSdk` 어댑터 인터페이스를 다룹니다.

> [!NOTE]
> `IActivePathCollector`, `IFileDialogAdapter`, `IInlineSearchAdapter` 구현체는 관리자 권한 창과의 UIPI 격리를 우회하고 안전하게 통신하기 위해 호스트에 의해 **특권 Hook 보조 프로세스**로 로드되어 실행됩니다.

## 1. 활성 경로 수집기 `IActivePathCollector`

포커스가 있는 활성 창에서 현재 작업 디렉토리를 추출하여 인라인 검색 범위 제한 및 상대 경로 탐색에 활용합니다.

```csharp
namespace Lertaro.PluginSdk;

public interface IActivePathCollector
{
    string Name { get; }
    string TargetName { get; }   // 대상 파일 관리자 이름 (예: "Directory Opus", "Total Commander")
    bool CanHandle(string className);
    string? TryGetPath(
        IntPtr activeHwnd, string activeClassName,
        IntPtr windowHwnd, string windowClassName,
        string processName);
}
```

- 포커스된 컨트롤(`activeHwnd`)과 상위 윈도우(`windowHwnd`)가 분리 전달되어 주소 표시줄이나 트리 뷰의 경로를 유연하게 추출할 수 있습니다.

## 2. 네이티브 파일 대화상자 어댑터 `IFileDialogAdapter`

Windows 표준 파일 열기/저장 대화상자를 감지하고 제어합니다.

```csharp
public interface IFileDialogAdapter
{
    string Name { get; }
    bool CanHandle(IntPtr hwnd, string className, string processName);
    string? GetCurrentPath(IntPtr hwnd);
    bool NavigateTo(IntPtr hwnd, string targetPath);
    bool TargetIsFolderOnly => false;  // 폴더 전용 선택 대화상자 여부
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => true;
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    bool RestoreFocus(IntPtr hwnd);
}
```

- **`TargetIsFolderOnly`**: `true`인 경우 사용자가 검색 결과에서 파일을 선택했을 때 `NavigateTo` 호출 전 부모 폴더로 자동 해석됩니다.
- **`AdapterRect`**: 픽셀 단위 물리 경계 `{ Left, Top, Right, Bottom }` 포함.

## 3. 인라인 검색 어댑터 `IInlineSearchAdapter`

파일 대화상자나 탐색기 내부에 Lertaro 검색바를 직접 임베드하여 양방향 선택 동기화를 구현합니다.

```csharp
public interface IInlineSearchAdapter
{
    string Name { get; }
    bool IsFileExplorer => false;      // Windows 파일 탐색기 여부
    bool CanHandle(IntPtr hwnd, string className, string processName);
    bool CanTrigger(IntPtr focusedHwnd, string className);
    bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor) => CanTrigger(hwndUnderCursor, classNameUnderCursor);
    bool CanEnterActionsMode(IntPtr hwnd);
    string? GetSearchScope(IntPtr hwnd);
    bool ExecuteItem(IntPtr hwnd, string path, string searchInput);
    bool GetDockBounds(IntPtr hwnd, out AdapterRect rect);
    IEnumerable<string> GetListItems(IntPtr hwnd) => [];
    void OnSelectionChanged(IntPtr hwnd, string path) { }
    void OnSearchFinished(IntPtr hwnd, bool executed) { }
}
```

## 4. 퀵 내비게이션 제공자 `IQuickNavigationProvider`

마우스 제스처로 열리는 [**퀵 내비게이션 메뉴**](../../user-guide/hotkeys#3-퀵-내비게이션-마우스-트리거)에 동적 그룹과 항목을 제공합니다.

```csharp
public interface IQuickNavigationProvider
{
    string GroupName { get; }           // 루트 그룹 제목
    Action<ISearchResult>? HeaderAction => null; // 헤더 행 우측 액션 버튼 (예: "+" 버튼)
    string? HeaderActionTooltip => null;// 액션 버튼 툴팁
    bool CanProvide(ISearchResult result);
    IEnumerable<DynamicMenuItem> GetMenuItems(ISearchResult result, IntPtr hMenu);
    void ExecuteCommand(ISearchResult result, uint commandId, IntPtr ownerHwnd);
    void ClearSession() { }
}
```

- **`HeaderAction`**: 루트 그룹 헤더에 액션 버튼을 추가할 수 있습니다(예: 북마크 프로바이더의 "현재 폴더 추가").
- **`DynamicMenuItem.IsHeader`**: 하위 메뉴에서 `IsHeader = true` 항목을 반환하여 서브메뉴 헤더 행을 렌더링할 수 있습니다.
