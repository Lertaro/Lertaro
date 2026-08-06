# 공유 추상화

다른 SDK 페이지의 인터페이스들 전반에서 사용되는 모델과 지원 계약입니다.

## `ISearchResult`

모든 플러그인 인터페이스가 다루는 결과의 읽기 전용 뷰입니다 — 플러그인은 절대 변경 가능한 결과 객체를
받지 않고, 오직 이것만 받습니다.

```csharp
interface ISearchResult
{
    string Name { get; }
    string FullPath { get; }
    string ContextDirectory { get; }
    bool IsDir { get; }
    bool IsApplication { get; }
    FileMetadata Metadata { get; }
    bool[]? GetHighlightMask(string text, string query);
}
```

`Metadata`는 호스트 자체의 파일 인덱스가 생성한 모든 결과에 대해 Size/Created/Modified/Accessed 값을
담고 있습니다 — 이를 읽는 데는 비용이 들지 않습니다(디스크 I/O나 IPC 없음). 이는
`FileMetadataService.GetMetadataAsync`([호스트 서비스](./services) 참고)와 다른데, 그 메서드는 현재의
결과 목록에 **이미 포함되어 있지 않은** 경로에 대해서만 호출할 가치가 있습니다.

## `FileMetadata`

```csharp
readonly record struct FileMetadata(long Size, DateTime Created, DateTime Modified, DateTime Accessed);
```

로컬 시간입니다. `default`(모든 필드가 0 또는 `DateTime.MinValue`)는 "값을 사용할 수 없음"을 의미합니다 —
파일 인덱스가 뒷받침하지 않는 결과(예: 다른 플러그인이 생성한 결과)일 때 그렇습니다. `Metadata.Modified !=
default`를 확인하면 이를 실제로 크기가 정당하게 0바이트지만 타임스탬프는 실제 값인 파일과 구분할 수
있습니다.

## `IPluginSearchWindow`

`ISearchResultAction.Execute`와 그와 유사한 콜백에 전달되는 최소한의 창 제어 표면입니다 — 의도적으로
작게 만들어져 있으며, 플러그인은 실제 창을 붙들고 있는 대신 이를 통해 결과에 대한 동작을 수행합니다.

```csharp
interface IPluginSearchWindow
{
    void LocateInExplorerExternal(string path);
    void OpenFileOrFolderExternal(string path);
    void OpenFileOrFolderAsAdminExternal(string path);
    void HideWindow();
}
```

## `IConfigurable`

`IPlugin`과 함께 이를 구현하면 **설정 → 플러그인 → 구성** 아래에 설정 UI가 자동으로 생성됩니다 — 단순한
경우라면 커스텀 WPF가 전혀 필요하지 않습니다.

```csharp
interface IConfigurable
{
    PluginConfigSchema GetConfigSchema();
}
```

`PluginConfigSchema`는 평면적인 `Fields: List<PluginConfigField>`입니다. 각 `PluginConfigField`는
`Key`, 선택적인 `GroupKey`/`LabelKey`/`DescriptionKey`(자신만의 `ITranslationProvider`가 있다면 이를
통해 해석되는 번역 키), `FieldType`, `DefaultValue`를 가지며, 타입에 따라 `Choices`, 중첩된 `SubFields`,
또는 `RequireModifier`(`Hotkey` 필드 전용, 수정자가 없는 단일 키를 거부)를 가질 수 있습니다.

필드(주로 `Text` 트리거 키워드)에 `RequireNonEmpty`를 설정하면, 저장 시 비어 있거나 공백뿐인 값을
그대로 유지하는 대신 `DefaultValue`로 되돌립니다 — 그렇지 않으면 사용자가 키워드 필드를 비웠을 때, 그
필드에 의존하는 무언가가 합리적인 기본값으로 돌아가는 대신 조용히 도달 불가능해질 수 있습니다.

`ConfigFieldType`은 다음을 다룹니다: `Boolean`, `Text`, `Integer`, `Choice`, `Array`, `Object`,
`Group`, `StringList`, `Hotkey`, `FilePath`, `FolderPath`. 중첩 그룹과 `StringList`를 사용하는 실제
스키마는 [CoreExtensions](../examples#coreextensions-—-동작과-셸-컨텍스트-메뉴)를
참고하세요.

## 레지스트리

`ActivePathCollectorRegistry`, `FileDialogAdapterRegistry`, `InlineSearchAdapterRegistry`는 호스트가
런타임에 해당 [시스템 어댑터 인터페이스](./system-adapters)의 모든 로드된 구현체를 한곳에 모으는 방법입니다.
플러그인 작성자가 이들과 직접 상호작용할 일은 보통 없습니다 — 인터페이스를 구현하는 것만으로 호스트가
자동으로 여러분의 플러그인을 발견하고 등록하기에 충분합니다.
