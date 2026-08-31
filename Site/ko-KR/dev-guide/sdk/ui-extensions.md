# UI 및 미리보기 확장

이 장에서는 메인 검색 윈도우 사이드바, 상세 테이블 커스텀 데이터 열, 퀵 패널 동적 탭, QuickLook 커스텀 파일 미리보기 렌더러, 썸네일 추출, WPF 테마 리소스 딕셔너리 및 다국어 확장을 위한 `Lertaro.PluginSdk` UI 인터페이스를 다룹니다.

## 1. 사이드바 필터 제공자 `ISidebarFilterProvider`

메인 검색 윈도우 좌측 사이드바에 커스텀 카테고리 필터 트리를 추가합니다.

```csharp
namespace Lertaro.PluginSdk;

public interface ISidebarFilterProvider : IPluginComponent
{
    IEnumerable<SidebarFilterGroup> GetFilterGroups();
}

public sealed class SidebarFilterGroup
{
    public required string GroupName { get; init; }
    public required IReadOnlyList<SidebarFilterItem> FilterItems { get; init; }
}

public sealed class SidebarFilterItem
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public ImageSource? Icon { get; init; }
    public required Func<ISearchResult, bool> FilterFunc { get; init; } // 일치 여부 판정 델리게이트
}
```

`SidebarFilterGroup.Id`는 선택 사항인 안정적인 그룹 식별자입니다. 호스트는 `Type`처럼 인식 가능한 식별자에 기본 동작을 적용할 수 있으며, 플러그인 전용 그룹이면 비워 두면 됩니다.

## 2. 테이블 커스텀 열 제공자 `IResultColumnProvider`

메인 검색 윈도우의 "자세히" 테이블 뷰에 커스텀 데이터 열을 추가합니다 (예: 미디어 재생 시간, 코드 줄 수, Git 브랜치명 등).

```csharp
public interface IResultColumnProvider : IPluginComponent
{
    string ColumnId { get; }
    string HeaderText { get; }
    double DefaultWidth => 120;
    double MinWidth => 40;
    bool IsVisibleByDefault => false;
    string? GetCellText(ISearchResult result);
    int Compare(ISearchResult a, ISearchResult b) => 0; // 헤더 클릭 시의 정렬 비교자
}
```

## 3. 퀵 패널 동적 탭 제공자 `IQuickPanelTabProvider`

퀵 검색창 하단의 [**퀵 패널**](../../user-guide/settings/quick-panel)에 동적 워크스페이스 탭을 제공합니다:

```csharp
public interface IQuickPanelTabProvider : IPluginComponent
{
    string TabId { get; }
    string Title { get; }
    string? IconPath => null;
    Task<IReadOnlyList<ISearchResult>> GetItemsAsync(CancellationToken token);

    // 드래그 앤 드롭 수신 로직
    bool CanHandleDragOver(IDataObject data) => false;
    Task HandleDropAsync(IDataObject data, CancellationToken token) => Task.CompletedTask;

    // 드래그를 통한 항목 순서 재배치 지원
    bool SupportsReorder => false;
    Task SaveOrderAsync(IReadOnlyList<ISearchResult> orderedItems) => Task.CompletedTask;

    // 탭 전용 컨텍스트 액션 메뉴 컨텍스트 생성
    DynamicActionContext CreateActionContext() => DynamicActionContext.Default;
}
```

## 4. 파일 즉시 미리보기 및 썸네일

### 커스텀 파일 미리보기 제공자 `IFilePreviewProvider`

QuickLook(스페이스바 미리보기) 창에서의 렌더링을 커스텀 구현합니다.

```csharp
public interface IFilePreviewProvider : IPluginComponent
{
    bool CanPreview(string filePath);
    int Priority => 0;                  // 여러 프로바이더 매칭 시 우선순위
    FrameworkElement CreatePreviewControl(string filePath);
}
```

#### 미리보기 생명주기 및 재사용 계약

반환된 WPF `FrameworkElement`가 다음 인터페이스를 구현하면 호스트가 렌더링 수명주기를 최적화합니다.

- **`IPreviewSessionAware`**: `void OnPreviewClosed()`를 구현하여 미리보기 종료 시 미디어 플레이어 핸들이나 WebView2 인스턴스, 파일 스트림을 안전하게 해제합니다.
- **`IReusablePreview`**: `void UpdatePreview(string filePath)`를 구현하여 방향키로 동종 파일 간을 이동할 때 컨트롤을 재성성하지 않고 내용만 갱신하여 화면 깜빡임을 방지합니다.

### 커스텀 썸네일 제공자 `IThumbnailProvider`

Shell 확장 프로그램이 없는 특수 포맷(`.blend`, `.psd`, `.dwg` 등)의 고해상도 썸네일을 추출합니다.

```csharp
public interface IThumbnailProvider : IPluginComponent
{
    bool CanProvide(string filePath);
    Task<ImageSource?> GetThumbnailAsync(string filePath, int targetSize, CancellationToken token);
}
```

## 5. 테마 및 다국어

### 커스텀 테마 제공자 `IThemeProvider`

커스텀 배색 및 WPF 리소스 딕셔너리를 제공합니다.

```csharp
public interface IThemeProvider : IPluginComponent
{
    string ThemeId { get; }
    string DisplayName { get; }
    ResourceDictionary GetResourceDictionary(bool isDark);
}
```

### 다국어 현지화 제공자 `ITranslationProvider`

플러그인 및 호스트에 동적 다국어 번역 딕셔너리를 제공합니다.

```csharp
public interface ITranslationProvider : IPluginComponent
{
    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```
