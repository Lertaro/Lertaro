# UI 및 미리보기 확장

## 결과 표시

### `ISidebarFilterProvider`

결과 사이드바에 분류용 필터 그룹을 추가합니다(예: 날짜 범위나 크기 구간).

```csharp
interface ISidebarFilterProvider
{
    int SortOrder { get; } // default 100; lower renders first
    IEnumerable<SidebarFilterGroup> GetFilterGroups();
}
```

`SidebarFilterGroup`은 `Header`, `AllowMultiSelect` 플래그(기본값 `false`. 이 그룹이 OR로 결합되는 다중
선택을 허용하도록 옵트인합니다 — 겹치거나 누적되는 날짜 범위처럼 한 번에 하나만 의미가 있는 항목이라면
꺼두세요), 그리고 `SidebarFilterItem`의 목록(Id, DisplayName, 선택적 아이콘, 현재 결과 목록에 대한 선택적
비동기 `FilterPredicate`)을 가집니다. 호스트는 그룹에 선택 항목이 생기면 지우기 버튼을 표시하므로,
제공자가 자체적으로 "전체"/"임의" 의사 항목을 둘 필요는 없습니다.

### `IResultColumnProvider`

결과 그리드 뷰에 추가 열(파일 크기, 수정 날짜, 커스텀 메타데이터 등)을 삽입합니다.

```csharp
interface IResultColumnProvider
{
    IEnumerable<ResultColumnDefinition> GetColumns();
    string GetCellValue(ISearchResult result, string columnId);
}
```

`ResultColumnDefinition`은 열 id, 헤더 텍스트, 너비, 선택적인 `VisibilityPredicate`/`SortComparer`
델리게이트를 가집니다.

## 빠른 패널

### `IQuickPanelTabProvider`

[빠른 패널](../../user-guide/settings/quick-panel) — 앞에 있는 창 위에 도킹되는 떠 있는 패널 — 에 탭
하나를 통째로 기여합니다. 탭은 컴포넌트의 이름을 달고 목록 하나를 담으며, 항목은 호스트 자신의 결과
행으로 그려지므로 아이콘, 열기, 썸네일, 동작 메뉴가 모두 공짜로 따라옵니다. CoreExtensions에는 다섯 개가
들어 있습니다: 즐겨찾기, 기록, Windows 최근 항목, 마지막 디렉터리, 최근 파일.

```csharp
interface IQuickPanelTabProvider : IPluginComponent
{
    Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
```

남의 탭 안의 그룹이 아니라 탭 그 자체입니다: 프로바이더가 돌려주는 것은 하나의 온전한 모음이며,
워크스페이스가 모으는 폴더들과는 직교하므로, 워크스페이스마다 하나하나 체크해 넣는 대신 그것들과
나란히 놓입니다.

`GetEntriesAsync()`는 패널을 불러낼 때마다 실행되며, 스트리밍이 아니라 완성된 집합을 돌려줍니다:
패널은 항목을 **하나의 집합으로** 정렬하고 잘라내므로(최신순, 최대 몇 개), 도착할 때마다 다시 정렬하지
않고서는 그중 절반만 보여 줄 수 없습니다. 이는 지연 시간의 대가를 치르지 않습니다 — 각 탭은 저마다의
작업에서 로드되고 패널은 가장 먼저 도착한 것에서 열리므로, 찾아 나서야 하는 프로바이더는 자기 탭만
늦춥니다. 그래도 토큰은 지켜 주세요: 패널이 닫힐 때 취소됩니다.

소스가 수정 시각을 안다면 `ISearchResult.Metadata`의 `Modified`를 채워 주세요 — 기본 최신순이 그것을
쓰며, 수정 시각이 없는 항목은 돌려준 순서를 유지합니다. 아무것도 돌려주지 않은 프로바이더에는 탭이
생기지 않고, 예외를 던진 프로바이더는 자기 탭 하나만 잃을 뿐 다른 탭에는 영향이 없습니다.

탭은 기본적으로 썸네일 타일로 열립니다. 사용자가 설정 → 빠른 패널 → 플러그인 탭에서 그 탭에
**목록으로 보기**를 체크한 경우는 예외입니다. 패널 머리글의 보기 전환은 패널이 열려 있는 동안 여전히
이것을 덮어씁니다. 탭을 **×**로 닫는 것과 설정 → 플러그인에서 컴포넌트를 끄는 것은 의도적으로 다른
일입니다: 앞은 스트립에서 빼기만 하고(같은 페이지에서 다시 체크하면 됩니다), 뒤는 아예 로드되지 않게
합니다. 호스트는 닫힘 상태와 표시 방식 모두를 컴포넌트 id를 안정 키로 삼아 저장하므로, 플러그인을 꺼 둔
사이에 닫은 탭은 플러그인이 돌아와도 닫힌 채입니다.

## 미리보기와 썸네일

### `IFilePreviewProvider`

특별히 처리하고 싶은 파일 형식에 대해 QuickLook 미리보기 창(자세한 내용은
[동작 메뉴 및 미리보기](../../user-guide/actions-and-preview#quicklook-미리보기) 참고)에 커스텀 WPF
`UIElement`를 렌더링합니다.

```csharp
interface IFilePreviewProvider
{
    string Name { get; }
    int Priority { get; } // default 0; higher runs first
    bool CanPreview(string path, bool isDir);
    UIElement CreatePreview(string path, bool isDir);
    bool RendersExternally { get; } // default false
}
```

`Priority`는 어디까지나 *기본* 순서일 뿐입니다 — 사용자는 설정 → 일반 →
[미리보기 및 썸네일](../../user-guide/settings/general#미리보기-및-썸네일)에서 (여러분의 제공자를
포함해) 제공자 순서를 자유롭게 재정렬할 수 있으며, 이것이 `Priority`가 반환하는 값보다 우선합니다.
여러분의 제공자가 선언한 우선순위가 실제로 실행되는 순서라고 가정하지 마세요.

두 개의 선택적인 동반 인터페이스가 미리보기 동작을 세밀하게 조정합니다.

- **`IPreviewSessionAware`** — 미리보기 제공자 자체가 비용이 큰 프로세스 외부 리소스(호스팅되는 네이티브
  핸들러, 파일 잠금 등)를 붙들고 있다면 이를 구현하세요. `EndPreviewSession()`은 개별 미리보기가 전환될
  때마다가 아니라 전체 미리보기 세션이 끝날 때 한 번 호출됩니다. 한 가지 예외로, `RendersExternally`가
  true인 제공자의 경우 세션이 끝날 때뿐 아니라 그 제공자에서 벗어나는 전환마다 호스트가 이를 호출합니다
  (아래 참고).
- **`IReusablePreview`** — `CreatePreview`가 반환한 `UIElement`가 완전히 새로 만들어지는 대신 새로운
  파일을 다시 가리킬 수 있다면 이를 구현하세요. `TrySetTarget(path, isDir)`는 변경 사항을 제자리에서
  처리했다면 `true`를, 호스트에게 새 미리보기를 만들도록 알리려면 `false`를 반환합니다.

`RendersExternally`는 실제 미리보기 표면이 `CreatePreview`가 반환하는 `UIElement`가 아니라 별도의,
외부에서 관리되는 창인 제공자를 위한 것입니다 — 예를 들어 파일을 완전히 다른 애플리케이션에 넘기는
경우입니다. 이 값이 설정된 제공자가 선정되면, 호스트는 `CreatePreview`의 콘텐츠를 표시하는 대신(그 콘텐츠는
실제로 보이지 않으므로 사소한 자리표시자여도 됩니다) 자체 미리보기 패널을 숨깁니다. 이를
**`IReceivesPreviewPanelBounds`** 와 함께 사용하면 호스트 자체 패널이 차지했을 정확한 화면 사각형(물리
픽셀)을 받아, 외부 창을 원래 나타날 위치 대신 그 자리에 배치할 수 있습니다.

```csharp
interface IReceivesPreviewPanelBounds
{
    void OnPreviewPanelBoundsAvailable(int left, int top, int width, int height);
}
```

실제 예시는 번들로 제공되는 (실험적인) QuickLook Bridge 플러그인을 참고하세요. 이 플러그인은 외부
[QuickLook](https://github.com/QL-Win/QuickLook) 앱을 자체 이름 있는 파이프를 통해 감지하고, 연결
가능하다면 모든 파일/폴더에 대해 그 앱의 창을 호스트 패널의 위치에 도킹시킵니다 — 사용자 대상 동작은
[동작 메뉴 및 미리보기 → QuickLook을 통한 외부 미리보기](../../user-guide/actions-and-preview#quicklook을-통한-외부-미리보기-선택-사항)를
참고하세요. 이는 이 코드베이스와 문서 전반에서 마찬가지로 비공식적으로 "QuickLook"이라고 불리는
Lertaro 자체의 내장 미리보기 패널과는 다른 것이라는 점에 유의하세요.

### `IThumbnailProvider`

일치하는 결과에 표시되는 아이콘/썸네일을 재정의합니다.

```csharp
interface IThumbnailProvider : IPluginComponent
{
    int Priority { get; } // default 0; higher runs first
    bool CanProvideThumbnail(string path, bool isDir);
    ImageSource? GetThumbnail(string path, int size);
}
```

위의 `IFilePreviewProvider.Priority`와 동일한 주의 사항이 적용됩니다. 이는 어디까지나 기본 순서일
뿐이며, 사용자는 설정 → 일반 →
[미리보기 및 썸네일](../../user-guide/settings/general#미리보기-및-썸네일)에서 이를 재정의할 수
있습니다(같은 탭에서 두 제공자의 순서 목록을 모두 다룹니다).

## 테마와 지역화

### `IThemeProvider` / `ITheme`

하나 이상의 커스텀 WPF 리소스 딕셔너리를 선택 가능한 테마로 등록합니다(**설정 → 일반 → 인터페이스 테마**에
표시됨).

```csharp
interface IThemeProvider
{
    string Name { get; }
    IEnumerable<ITheme> GetThemes();
}

interface ITheme
{
    string Id { get; }
    string DisplayName { get; }
    bool IsDark { get; }
    double WindowOpacity { get; } // default 1.0
    ResourceDictionary GetResources();
}
```

### `ITranslationProvider`

특정 문화권에 대한 UI 문자열을 제공합니다 — 플러그인 자체의 UI를 위한 것일 수도 있고, `PinyinAlias`처럼
단순히 자신의 표시 이름만을 위한 것일 수도 있습니다. 이를 무관한 다른 인터페이스와 같은 클래스에 함께
구현한 플러그인 예시는 [예제 플러그인](../examples)을 참고하세요.

```csharp
interface ITranslationProvider
{
    string Name { get; }
    IReadOnlyList<string> SupportedCultures { get; } // e.g. "zh-CN", "en-US"
    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```

`TranslationService.LoadEmbeddedTranslations`([호스트 서비스](./services) 참고)는 이를 플러그인 DLL에
내장된 JSON 파일로 뒷받침하는 표준적인 방법입니다.
