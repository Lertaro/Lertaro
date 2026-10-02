# UI 및 미리보기 확장

이 장에서는 검색 창 사이드바를 확장하고, 커스텀 테이블 열을 추가하며, 퀵 패널 동적 탭을 제공하고, QuickLook 파일 미리보기 렌더러와 썸네일 추출자를 만들고, WPF 테마와 다국어 언어 팩을 배포하기 위한 `Lertaro.PluginSdk` 인터페이스를 다룹니다.

이 인터페이스들은 모두 `Lertaro.PluginSdk.Abstractions.Plugins` 아래에 위치하며(미리보기 제공자는 `…Abstractions.Plugins.Preview`) 전부 `IPluginComponent`에서 파생됩니다. `IPluginComponent`가 제공하는 `Name`이 **설정 → 플러그인**에 표시되는 이름입니다.

## 1. 사이드바 필터 제공자 `ISidebarFilterProvider`

검색 창 좌측 사이드바에 커스텀 필터 카테고리를 주입합니다:

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface ISidebarFilterProvider : IPluginComponent
{
    IEnumerable<SidebarFilterGroup> GetFilterGroups();

    // 정렬 가중치. 값이 낮을수록 먼저 렌더링된다.
    int SortOrder => 100;
}

public class SidebarFilterGroup
{
    // 호스트가 잘 알려진 그룹에 대해 인식하는 선택적 안정 ID (내장 결과 유형 필터의 "Type" 등).
    // 그룹이 전적으로 플러그인에서 정의된 것이라면 비워 둔다.
    public string Id { get; set; } = string.Empty;
    public string Header { get; set; } = string.Empty;
    public List<SidebarFilterItem> Items { get; set; } = new();

    // 이 그룹에서 여러 항목을 동시에 활성 상태로 둘 수 있는지 여부.
    public bool AllowMultiSelect { get; set; }
}

public class SidebarFilterItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    // 아이콘 경로 두 가지, 모두 테마를 인식한다. IconData는 활성 테마의 텍스트 색으로 그리는
    // 글리프, IconKey는 호스트가 이미 소유한 리소스 이름이다. 아이콘이 없으려면 둘 다 null로 둔다.
    public string? IconData { get; set; }
    public string? IconKey { get; set; }

    // 이 항목과 일치하려면 결과가 만족해야 하는 조건자. 기본값은 "아무것도 일치시키지 않음"이므로,
    // 한 번도 설정하지 않은 항목은 표시되지만 아무것도 선택할 수 없다.
    public Func<ISearchResult, bool> MatchPredicate { get; set; } = _ => false;
}
```

그룹과 항목은 record가 아니라 변경 가능한 클래스입니다: 필요한 속성만 채우고 나머지는 기본값으로 두면 됩니다.

## 2. 테이블 커스텀 열 제공자 `IResultColumnProvider`

전체 검색 창 "자세히" 테이블 뷰에 커스텀 데이터 열을 추가합니다(예: 미디어 재생 시간, 코드 줄 수, Git 브랜치). 제공자는 열을 한 번 기술하고, 셀 값은 요청될 때마다 반환합니다:

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IResultColumnProvider : IPluginComponent
{
    IEnumerable<ResultColumnDefinition> GetColumns();
    string GetCellValue(ISearchResult result, string columnId);
}

public class ResultColumnDefinition
{
    public string ColumnId { get; set; } = string.Empty;
    public string HeaderText { get; set; } = string.Empty;
    public double Width { get; set; } = 120;

    // 선택 사항: 이 열이 적용되지 않는 결과에서는 열을 숨린다.
    public Func<ISearchResult, bool>? VisibilityPredicate { get; set; }

    // 선택 사항: 헤더 클릭 시 커스텀 정렬. x < y이면 음수, x > y이면 양수.
    public Func<ISearchResult, ISearchResult, int>? SortComparer { get; set; }

    // 선택 사항: 전체 검색 창에서 이 열의 셀을 더블클릭했을 때의 동작.
    // 설정하지 않으면 셀 더블클릭이 행의 다른 어디를 더블클릭하든 같은 동작이 된다.
    public Action<ISearchResult>? OnDoubleClick { get; set; }
}
```

`GetCellValue`는 목록 렌더링 중에 호출되므로 값이 싸야 합니다. 디스크를 건드리지 말고 미리 계산해 둔 값이나 캐시를 반환하세요.

## 3. 퀵 패널 동적 탭 제공자 `IQuickPanelTabProvider`

[**퀵 패널**](../../user-guide/settings/quick-panel)에 동적 워크스페이스 탭을 제공합니다:

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IQuickPanelTabProvider : IPluginComponent
{
    // 지금 표시할 항목. 패널이 소환될 때마다 호출된다.
    Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
```

그 단 하나의 메서드가 계약의 전부입니다 — 드롭 처리나 순서 재배치나 액션 컨텍스트처럼 구현할 다른 것은 없습니다.

- `CancellationToken`은 패널이 닫힐 때 취소됩니다. 이를 관찰하는 것은 탭 자신의 목록뿐이고, 플러그인의 그 밖의 동작은 바뀌지 않습니다.
- 소스가 알고 있다면 `ISearchResult`의 `Metadata.Modified`를 채우세요. 기본 최신 우선 정렬이 이 값을 사용하기 때문입니다. 기본값으로 두면 반환한 순서가 그대로 유지됩니다.
- 아무것도 반환하지 않는 제공자에는 탭이 생기지 않으며, 이를 위해 설정할 항목은 없습니다.
- 탭은 사용자가 직접 추가해야 하는 폴더와 달리 플러그인이 존재하는 순간 함께 존재합니다. 탭 스트립에서 닫고 **설정 → 퀵 패널**에서 다시 열 수 있는데, 이는 **설정 → 플러그인**에서 컴포넌트를 비활성화하는 것(아예 로드되지 않게 함)과는 별개의 문제입니다.

## 4. 파일 미리보기 및 썸네일

### 커스텀 파일 미리보기 제공자 `IFilePreviewProvider`

QuickLook 패널 내부의 미리보기를 렌더링합니다. 사용자는 `Alt+P` 또는 미리보기 가능한 행 휠클릭으로 패널을 엽니다(참고: [**액션 메뉴 및 즉시 미리보기**](../../user-guide/actions-and-preview)):

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins.Preview;

public interface IFilePreviewProvider : IPluginComponent
{
    // Priority는 동률 처리에만 쓰입니다. 사용자가 설정해 둔 제공자 순서(설정 → 일반 →
    // 미리보기 및 썸네일)가 먼저 적용되고, Priority는 그 안에서 정렬하며 높은 쪽이 앞에 선다.
    int Priority => 0;

    bool CanPreview(string path, bool isDir);
    UIElement CreatePreview(string path, bool isDir);

    // 패널 안에 배치할 WPF 콘텐츠를 반환하는 대신 외부 창 자체를 호스팅하는 경우 true
    // (QuickLook 브리지 플러그인이 이렇게 한다).
    bool RendersExternally => false;
}
```

#### 미리보기 생명주기 및 재사용 계약

아래 첫 계약을 **제공자**가 구현하거나, 반환하는 `UIElement`가 두 번째 계약을 구현하면 호스트가 미리보기 생명주기를 최적화합니다.

- **`IPreviewSessionAware`** — 반환한 컨트롤이 아니라 **제공자**에 캐스팅되는 `void EndPreviewSession();`. 제공자는 프로세스 안의 컨트롤만이 아니라 실제 외부 창을 소유합니다(`HwndHost`, 또는 네이티브 `IPreviewHandler`와 그 `prevhost` 서로게이트). 그래서 소유 창이 닫힐 때 세션을 종료하도록 통보받고, 프로세스 안에서 렌더링하는 제공자의 경우에는 미리보기 패널이 가려지거나 세션이 끝날 때만 통보받습니다. 이것이 없으면 호스트의 창이 아무 참조도 없이 남게 됩니다.
- **`IReusablePreview`** — 반환한 요소에 캐스팅되는 `bool TrySetTarget(string path, bool isDir);` 사용자가 방향키로 유사한 파일 사이를 이동할 때 호스트는 컨트롤을 폐기하고 다시 만드는 대신 같은 컨트롤에 대상 재지정을 요청하며, 이것이 깜빡임을 없애 줍니다. 새 대상이 이 인스턴스에 맞지 않으면 `false`를 반환하고, 호스트는 새 미리보기를 만드는 방식으로 물러납니다.
- **`IReceivesPreviewPanelBounds`** — `void OnPreviewPanelBoundsAvailable(int left, int top, int width, int height);` 자기 프로세스 밖의 창을 호스팅하는 제공자는 패널이 차지하는 사각형을 알아야 그 안에 창을 넣거나 배치할 수 있습니다. 그 사각형이 확인되는 대로 통보받으려면 이 계약을 구현하세요.

### 커스텀 썸네일 제공자 `IThumbnailProvider`

네이티브 Shell 핸들러가 없는 형식(`.blend`, `.psd`, `.dwg`)의 썸네일을 추출합니다:

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins.Preview;

public interface IThumbnailProvider : IPluginComponent
{
    // 미리보기와 같은 규칙이다. 사용자가 설정해 둔 썸네일 제공자 순서가 먼저 정하고,
    // Priority는 그 안에서만 정렬한다.
    int Priority => 0;

    bool CanProvideThumbnail(string path, bool isDir);

    // 동기식이다. 결과 목록 렌더링 경로에서 실행되기 때문이다 -- 빠르게 유지할 것.
    // 메모이제이션할 필요가 없다. 호스트가 반환한 결과를 캐시하기 때문이다(물리 항목이나
    // 가상 항목은 경로 기준으로, 그 외에는 확장자 기준으로). 결론은 두 가지 -- `size`는 셸
    // 이미지 목록에서 가져온 호스트 자신의 선택이므로 특정한 값을 기대하지 말 것, 그리고
    // 제공자는 디렉터리에 대해 아예 질문받지 않는다.
    ImageSource? GetThumbnail(string path, int size);
}
```

## 5. 테마 및 다국어

### 테마 제공자 `IThemeProvider`

색 팔레트와 WPF 리소스 딕셔너리를 제공합니다:

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IThemeProvider : IPluginComponent
{
    IEnumerable<ITheme> GetThemes();
}
```

```csharp
namespace Lertaro.PluginSdk.Abstractions;   // 참고: 테마 자체는 한 단계 위입니다

public interface ITheme
{
    string Id { get; }
    string DisplayName { get; }
    bool IsDark { get; }
    ResourceDictionary GetResources();

    // 1.0 미만에서는 호스트의 레이어드 서피스 헬퍼로 만들어진 창이 모서리를 직접 그리고
    // 클리핑해야 하는 레이어드 반투명 창이 되고, 1.0에서는 불투명으로 남아 창 관리자가
    // 모서리를 둥글게 처리하며 ClearType이 유지된다. 이 선택은 생성자에서 한 번 이루어지는데,
    // 핸들이 만들어진 뒤에는 AllowsTransparency를 바꿀 수 없기 때문이다. 현재는 알림 창에
    // 적용되며, 창이 화면에 떠 있는 동안 테마가 바뀌어도 창은 다시 만들어지지 않는다.
    double WindowOpacity => 1.0;
}
```

제공자 하나는 원하는 만큼 많은 테마를 제공할 수 있고, 제공자가 어두운 변형을 따로 노출하는 대신 각 테마가 스스로 명암 여부를 나타내는 플래그를 가집니다.

### 다국어 현지화 제공자 `ITranslationProvider`

번역 딕셔너리를 동적으로 제공합니다:

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface ITranslationProvider : IPluginComponent
{
    // 이 제공자가 처리할 수 있는 컬처 코드로, 호스트가 아무것도 로드되기 전에 설정에서 이를
    // 제시할 수 있다. 기본값은 비어 있으며 "요청되는 것으로부터 발견한다"를 뜻한다.
    IReadOnlyList<string> SupportedCultures => Array.Empty<string>();

    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```
