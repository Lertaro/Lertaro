# 공유 추상화 계약

이 장에서는 `Lertaro.PluginSdk` 전체에서 공통으로 사용되는 핵심 데이터 모델, 읽기 전용 계약 및 스키마 기반 설정 추상화를 정리합니다.

## 1. 검색 결과 모델 `ISearchResult`

Lertaro 아키텍처에서 플러그인은 검색 결과에 대해 항상 읽기 전용 인터페이스 `ISearchResult`를 통해 접근합니다.

```csharp
namespace Lertaro.PluginSdk;

public interface ISearchResult
{
    string Name { get; }                  // 표시 이름 (예: "Lertaro.exe")
    string FullPath { get; }              // 절대 물리 경로 (예: "C:\Program Files\Lertaro\Lertaro.exe")
    string ContextDirectory { get; }      // 부모 디렉토리 경로 (예: "C:\Program Files\Lertaro")
    bool IsDir { get; }                   // 디렉토리 여부
    bool IsApplication { get; }           // 실행 파일 또는 바로가기 여부
    FileMetadata Metadata { get; }        // 고성능 파일 메타데이터 (크기, 수정일 등)
    bool[]? GetHighlightMask(string text, string query); // 문자 단위 하이라이트 마스크 계산
}
```

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
| **`Text`** | 텍스트 입력 상자. `RequireNonEmpty` 활성화 시 빈 문자열일 때 `DefaultValue`로 자동 폴백. |
| **`Integer`** | 최솟값과 최댓값을 지정할 수 있는 숫자 조절 상자. |
| **`Choice`** | `Choices` 목록에서 선택하는 드롭다운. |
| **`Hotkey`** | 키 녹화 컨트롤(`RequireModifier = true`로 수식키 필수화 가능). |
| **`FilePath` / `FolderPath`** | 찾아보기 대화상자 버튼이 포함된 경로 입력 컨트롤. |
| **`StringList`** | 항목 추가, 삭제, 순서 변경이 가능한 다중 행 목록 상자. |
| **`Group`** | 접을 수 있는 카드 형태의 하위 필드 그룹(`SubFields`). |
| **`CustomControl`** | 커스텀 WPF `UIElement` 컨트롤을 직접 임베드. |
| **`Button`** | 작업 버튼을 표시하고 필드의 `OnClick` 델리게이트를 호출하며 설정 값은 저장하지 않습니다. |

`PluginConfigSchema`는 `OnSave` 및 `OnRollback` 생명주기 델리게이트를 지원하여 저장 및 취소 시의 커스텀 로직을 처리할 수 있습니다.

## 5. 전체 검색 창 파일 결과 `IFullSearchFileResultProvider`

전체 검색 창에 실제 파일 또는 폴더 행을 추가해야 하는 플러그인은 `IFullSearchFileResultProvider`를 구현할 수 있습니다.

```csharp
public interface IFullSearchFileResultProvider : IPluginComponent
{
    IReadOnlyList<InstantResultItem> GetFileResults(string query, int limit);
}
```

호스트는 전체 검색 창의 최종 렌더링 단계에서만 `GetFileResults`를 호출합니다. 현재 쿼리를 처리하지 않을 때는 빈 목록을 반환하세요. 반환하는 각 `InstantResultItem`은 실제로 존재하는 파일 또는 폴더를 나타내야 전체 검색 창의 경로, 크기, 유형 열을 의미 있게 표시할 수 있습니다. 이 구성 요소는 플러그인의 즉시 결과 제공자와 동일한 활성화/비활성화 스위치로 관리됩니다.
