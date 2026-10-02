# 공유 추상화 계약

이 장에서는 `Lertaro.PluginSdk` 전체에서 공통으로 사용되는 핵심 데이터 모델, 읽기 전용 계약 및 스키마 기반 설정 추상화를 정리합니다.

## 1. 검색 결과 모델 `ISearchResult`

Lertaro 아키텍처에서 플러그인은 검색 결과에 대해 항상 읽기 전용 인터페이스 `ISearchResult`를 통해 접근합니다.

```csharp
namespace Lertaro.PluginSdk.Abstractions;

public interface ISearchResult
{
    string Name { get; }                  // 표시 이름 (예: "Lertaro.exe")
    string FullPath { get; }              // 절대 물리 경로 (예: "C:\Program Files\Lertaro\Lertaro.exe")
    string ContextDirectory { get; }      // 부모 디렉토리 경로 (예: "C:\Program Files\Lertaro")
    bool IsDir { get; }                   // 디렉토리 여부
    bool IsApplication { get; }           // 실행 파일 또는 바로가기 여부
    bool[]? GetHighlightMask(string text, string query) => null; // 문자 단위 하이라이트 마스크 계산
    FileMetadata Metadata => default;     // 고성능 파일 메타데이터 (크기, 수정일 등)
    string? InstantActionArgument => null; // 즉시 결과가 가리키는 대상
}
```

`FullPath`는 대부분의 액션이 기준으로 삼는 식별자이므로, 경로가 아닌 대상을 다루는 즉시 결과(`activatewindow:12345`, `kill:4321`, 커스텀 명령 페이로드)는 그 대상을 `InstantActionArgument`에 담고, 제공자는 그곳에서 읽습니다. 그 밖의 모든 행 — 일반 파일 및 폴더 결과, 플러그인 검색 액션, 기록 항목 — 에서는 `null`로 남습니다.

> [!NOTE]
> `ISearchResult.Metadata`는 인메모리 USN/MFT 인덱스에서 직접 주입되므로 **이 속성에 접근할 때 디스크 I/O나 IPC 호출이 전혀 발생하지 않습니다**. 결과 세트에 포함되지 않은 외부 경로를 조회할 때만 `FileMetadataService.GetMetadataAsync`를 호출하세요.

## 2. 파일 메타데이터 구조체 `FileMetadata`

```csharp
public readonly record struct FileMetadata(
    long Size,
    DateTime Created,
    DateTime Modified,
    DateTime Accessed
);
```

- 타임스탬프는 모두 **로컬 시간 (Local Time)**입니다.
- `Metadata == default`인 경우(필드가 0 또는 `DateTime.MinValue`) 물리 인덱스가 아닌 플러그인이 동적으로 생성한 결과임을 나타냅니다.
- `Metadata.Modified != default`로 메타데이터 미제공 상태와 실제 존재하는 0바이트 파일을 정확히 구분할 수 있습니다.

## 3. 호스트 윈도우 제어 인터페이스 `IPluginSearchWindow`

액션 실행 콜백(`ISearchResultAction.Execute` 등)이 호출될 때 호스트 윈도우 작업을 안전하게 트리거하기 위해 전달됩니다:

```csharp
public interface IPluginSearchWindow
{
    void LocateInExplorerExternal(string path);       // 탐색기 등에서 파일 위치 강조 표시
    void OpenFileOrFolderExternal(string path);       // 기본 앱으로 일반 실행
    void OpenFileOrFolderAsAdminExternal(string path);// 관리자 권한으로 실행
    void HideWindow();                                // 현재 검색창 숨기기
}
```

## 4. 스키마 기반 설정 시스템 `IConfigurable`

플러그인에서 사용자 설정 항목을 제공해야 하는 경우 `IConfigurable`을 구현하면 XAML 작성 없이도 **설정 → 플러그인 → 구성**에 네이티브 폼 UI가 자동 렌더링됩니다:

```csharp
public interface IConfigurable
{
    PluginConfigSchema GetConfigSchema();
}
```

### 지원 필드 타입 `ConfigFieldType`

| 필드 타입 | 컨트롤 및 동작 설명 |
| :--- | :--- |
| **`Boolean`** | 토글 스위치 또는 체크박스. |
| **`Text`** | 텍스트 입력 상자. 사용자가 값을 지우면 `RequireNonEmpty`가 `DefaultValue`로 폴백하고, `MaxLength`가 길이를 제한하며(0이거나 설정하지 않으면 제한 없음), `SelectionStart` / `SelectionLength`는 이 필드에서 열리는 입력 대화상자의 0부터 시작하는 초기 선택 범위를 지정합니다. |
| **`Integer`** | 최솟값과 최댓값을 지정할 수 있는 숫자 조절 상자. |
| **`Choice`** | `Choices` 또는 `ChoiceOptions` 목록에서 선택하는 드롭다운. |
| **`Array`** | 목록 값입니다. `SubFields`가 있으면 **레코드**의 목록으로 렌더링되어 마스터/디테일 편집기(항목당 중첩 폼 하나 — 파일 필터, 커스텀 명령, 웹 검색 플러그인이 사용하는 형태)가 되고, `SubFields`가 없으면 스칼라 단순 목록으로 컴팩트한 단일 열 편집기가 됩니다. SDK는 `DefaultValue`에 기본값을 아예 주지 않습니다(선언에서는 `object?`, `null!`). 그래서 저장소 안의 모든 플러그인은 빈 목록을 나타내려고 `new List<object>()`를 넘깁니다. |
| **`Object`** | `SubFields`로 편집하는 하나의 구조화된 값이며, `Array`가 제공하는 목록 장치는 없습니다. |
| **`Group`** | 접을 수 있는 카드 형태의 하위 필드 그룹(`SubFields`). |
| **`StringList`** | 항목 추가, 삭제, 순서 변경 및 자동 줄바꿈을 지원하는 여러 줄 목록 상자이며, 실제 줄바꿈은 시각적 표시로만 나타나고 설정 값에는 포함되지 않습니다. |
| **`Hotkey`** | 키 녹화 컨트롤(`RequireModifier = true`로 수식키 필수화 가능). |
| **`FilePath` / `FolderPath`** | 찾아보기 대화상자 버튼이 포함된 경로 입력 컨트롤. |
| **`CustomControl`** | 커스텀 WPF `UIElement` 컨트롤을 직접 임베드합니다(`CustomControl` 필드로도 지정할 수 있습니다). |
| **`Button`** | 작업 버튼을 표시하고 필드의 `OnClick` 델리게이트를 호출하며 설정 값은 저장하지 않습니다. |

`PluginConfigField`의 나머지 멤버들은 호스트가 위 타입들을 놓고 렌더링하거나 저장하는 것들입니다: `Key`(저장되는 설정 이름), `GroupKey`(필드가 들어가는 `Group` 카드), `LabelKey` / `DescriptionKey`(일반 텍스트가 아니라 번역 키), `RequireNonEmpty`, `Choices` / `ChoiceOptions` / `SubFields`, `IsTriggerWord`([**검색 코어 및 액션**](./core-search-actions)의 "트리거 단어" 참조), `MaxLength`, `SelectionStart` / `SelectionLength`, `CustomControl`, `OnClick`, 그리고 플러그인이 호스트의 설정 저장소가 아닌 다른 곳에 값을 보관할 수 있게 하는 두 델리게이트 `Func<object?>? GetValue`와 `Action<object?>? SetValue`.

### 아이콘 필드

스키마 키가 `Icon`인 텍스트 필드에는 아이콘 미리보기가 표시됩니다. WPF Path Data를 직접 입력할 수 있으며, 전체 SVG/XML 문서를 붙여 넣으면 호스트가 모든 `<path d>` 값을 추출해 결합하고 변환된 WPF Path Data만 저장합니다. 유효하지 않은 아이콘 내용은 지워지고 테마가 적용된 오류 대화상자로 알립니다. 아이콘을 지정하지 않을 때는 빈 값도 유효합니다.

`PluginConfigSchema`는 `OnSave` 및 `OnRollback` 생명주기 델리게이트를 지원합니다. `OnSave`는 사용자가 **확인/적용**을 눌러 변경 사항을 커밋할 때 실행되고, `OnRollback`은 취소하거나 되돌릴 때 상태를 복원합니다.

### 지역화된 선택 항목 레이블

안정적인 설정 값을 유지하면서 지역화된 레이블을 표시해야 하는 선택 항목에는 `ChoiceOptions`를 사용합니다. `PluginConfigChoice.Value`는 플러그인 설정에 저장되고 `LabelKey`는 화면에 표시할 텍스트로 해석됩니다. 저장 값과 표시 텍스트가 같다면 기존 `Choices` 컬렉션을 사용하면 됩니다.

```csharp
new PluginConfigField
{
    Key = "DisplayMode",
    FieldType = ConfigFieldType.Choice,
    DefaultValue = "FriendlyName",
    ChoiceOptions =
    [
        new PluginConfigChoice
        {
            Value = "FriendlyName",
            LabelKey = "DisplayMode_FriendlyName"
        }
    ]
}
```

## 5. 전체 검색 창 파일 결과 `IFullSearchFileResultProvider`

전체 검색 창에 실제 파일 또는 폴더 행을 추가해야 하는 플러그인은 `IFullSearchFileResultProvider`를 구현할 수 있습니다.

```csharp
public interface IFullSearchFileResultProvider : IPluginComponent
{
    IReadOnlyList<InstantResultItem> GetFileResults(string query, int limit);

    // 선택 사항입니다. 기본 구현은 GetFileResults를 순회하므로 이 멤버가 생기기 전에 작성된 제공자는 그대로 동작합니다.
    IEnumerable<InstantResultItem> GetFileResultsStreamed(string query, int limit);
}
```

호스트는 전체 검색 창 자신의 파일 검색이 아직 스트리밍되는 동안 백그라운드 스레드에서 제공자를 호출하고, 검색이 확정되기를 기다리지 않고 결과가 도착하는 순서대로 렌더링합니다. 현재 쿼리를 처리하지 않을 때는 빈 목록을 반환하세요. 반환하는 각 `InstantResultItem`은 실제로 존재하는 파일 또는 폴더를 나타내야 전체 검색 창의 경로, 크기, 유형 열을 의미 있게 표시할 수 있습니다. 응답에 수 초가 걸리는 제공자(예: 전문 인덱스 순회)는 `GetFileResultsStreamed`를 재정의해 찾히는 대로 항목을 넘길 수 있으며, 그러면 검색은 계속되는 동안 첫 몇 줄이 먼저 화면에 나타납니다. 기본 구현은 `GetFileResults`를 순회할 뿐이므로 재정의는 선택 사항입니다. 이 구성 요소는 **설정 → 플러그인**에 자기 컴포넌트 유형을 키로 한 **자체** 활성화/비활성화 스위치를 가집니다 — 플러그인의 즉시 결과 제공자를 꺼도 이 구성 요소는 꺼지지 않고, 그 반대도 마찬가지입니다.

## 6. 사용자 설정 경로 확인 `UserPathResolver`

플러그인이 사용자가 입력했거나 설정에 저장된 경로를 받을 때는 파일 시스템 API를 호출하기 전에 `Lertaro.PluginSdk.Helpers.UserPathResolver`를 사용하여 환경 변수와 Windows 셸 가상 경로를 동일한 규칙으로 처리하세요.

```csharp
string expanded = UserPathResolver.Expand(rawPath);            // 인자는 string?, 반환은 string
bool isVirtual = UserPathResolver.IsVirtualPath(expanded);
string resolved = UserPathResolver.Resolve(rawPath);           // 아래에 선택적 두 번째 인수가 있음

// Resolve와 ResolveForNavigation 모두 선택적인 Func<string, string>?를 받습니다. 셸이 해석하지
// 못하는 가상 토큰을 파일 시스템에 묻기 전에 실제 경로로 바꿔 주는 델리게이트이고, 리졸버가 없고
// 해석할 것도 없으면 마지막 수단으로 입력값이 그대로 돌아옵니다.

// 경로를 열거나 탐색하려 할 때는 Resolve가 아니라 ResolveForNavigation을 쓰세요. 호스트가
// 탐색할 수 있는 파일 시스템 대상으로 가상 셸 항목까지 정규화해서 돌려받습니다.
string target = UserPathResolver.ResolveForNavigation(rawPath);
```

`Expand`는 앞뒤 공백을 제거하고 `%USERPROFILE%` 같은 환경 변수를 확장합니다. `Resolve`는 확장 후 `shell:Downloads` 또는 `::{CLSID}` 같은 토큰을 가능한 경우 실제 경로로 확인합니다. `shell:AppsFolder`처럼 실제 경로가 없는 가상 폴더는 대신 정규 `::{CLSID}` 이름으로 확인되므로 같은 폴더의 여러 표기가 서로 일치합니다. 그 결과는 여전히 가상 경로입니다. 셸이 전혀 해석할 수 없는 토큰만 변경하지 않고 반환됩니다. 파일 시스템 API에 전달하기 전에 `IsVirtualPath`로 결과를 확인하세요. 디렉터리 인덱싱 API는 실제 폴더로 확인되고 호스트 인덱스 대상인 경로만 열거할 수 있습니다.
