# 호스트 제공 서비스

`Lertaro.PluginSdk.Services` 네임스페이스는 호스트 내부의 핵심 알고리즘, 캐시, 플랫폼 연동 기능을 플러그인에서 직접 활용할 수 있도록 고성능 정적 서비스를 제공합니다.

## 1. 핵심 정적 서비스 요약

| 서비스명 | 주요 메서드 및 시그니처 | 기능 설명 |
| :--- | :--- | :--- |
| **`FuzzyMatchService`** | `bool IsMatch(string pattern, string text)`<br>`bool[]? GetHighlightMask(string text, string query)`<br>`double GetMatchScore(string text, string query)` | 호스트와 동일한 fzf 퍼지 매칭 엔진을 실행하고 문자 단위 하이라이트 마스크를 계산하며, 일관된 결과 정렬에 사용할 매칭 품질 점수를 제공합니다. |
| **`TranslationService`** | `string Get(string key)`<br>`bool TryGet(string key, out string result)`<br>`string Format(string key, params object[] args)`<br>`string GetCurrentCulture()`<br>`IReadOnlyList<string> GetSupportedCultures(Assembly assembly)`<br>`Dictionary<string, string> LoadEmbeddedTranslations(Assembly assembly, string cultureKey, string typeName)`<br>`event Action<string>? CultureChanged` | 다국어 동적 파싱 및 런타임 언어 변경 브로드캐스트. `GetCurrentCulture()`는 OS 언어가 아닌 설정 센터에서 선택된 UI 언어 코드(예: `"ko-KR"`)를 반환하며, `CultureChanged`를 구독하여 UI 언어 변경 시 사전 재로드 및 내부 상태를 갱신할 수 있습니다. `TryGet`은 키가 해석되었는지를 답하고, `Get`은 예외를 던지는 대신 눈에 보이는 `[key]` 자리 표시자로 물러납니다. `GetSupportedCultures(assembly)`는 그 어셈블리의 임베디드 리소스가 커버하는 컬처들을 나열하고, `LoadEmbeddedTranslations(assembly, cultureKey, typeName)`은 한 컬처의 딕셔너리를 반환합니다. |
| **`IconService`** | `ImageSource? GetIcon(string path, bool isDir)`<br>`ImageSource? GetThumbnail(string path, int size)`<br>`ImageSource? GetIconFromCacheOnly(string path, bool isDir, out bool needsLoad)` | 메모리 및 디스크 캐시가 적용된 Windows Shell 파일 아이콘 및 썸네일 추출. `GetIconFromCacheOnly`는 Shell을 건드리지 않습니다: 이미 캐시된 것만 반환하고 `needsLoad`로 실제 로드가 아직 필요한지 알리는데, 이 방식으로 목록이 먼저 그려진 뒤 아이콘을 사후에 채워 넣을 수 있습니다. |
| **`FavoritesService`** | `IEnumerable<FavoriteItem> GetFavorites()`<br>`bool IsFavorite(string path)`<br>`bool TryAddFavorite(FavoriteItem favorite)` | 즐겨찾기 목록 조회, 경로의 등록 여부 확인, 호스트 브리지를 통한 즐겨찾기 추가를 제공합니다. |
| **`HistoryService`** | `IEnumerable<HistoryEntry> GetHistoryEntries()` | 최근 열어본 순서대로 정렬된 검색 기록(키워드, 파일 유형 및 항목별 사용 횟수 포함)을 조회합니다. 동일한 실제 경로는 가장 최근에 연 키워드 아래에 최대 한 번만 표시됩니다. |
| **`FileMetadataService`** | `Task<IReadOnlyDictionary<string, FileMetadata>> GetMetadataAsync(IReadOnlyList<string> paths)` | 현재 검색 결과에 포함되지 않은 외부 경로의 파일 크기 및 타임스탬프 일괄 조회. |
| **`DirectoryIndexerService`** | `void RegisterDirectory(string pluginId, string directoryPath, bool recursive = true, string filterPattern = "*")`<br>`void UnregisterDirectories(string pluginId)`<br>`IDisposable WatchDirectories(string pluginId, Action onChanged)`<br>`IDisposable WatchDirectories(string pluginId, Action<IReadOnlyList<string>> onChanged)`<br>`void NotifyDirectoryChanged(string pluginId)`<br>`void NotifyDirectoryChanged(string pluginId, IReadOnlyList<string> changedDirectories)`<br>`Task<List<ISearchResult>> SearchDirectoriesAsync(string pluginId, string query, CancellationToken token = default)`<br>`IAsyncEnumerable<ISearchResult> EnumerateDirectoryAsync(string directoryPath, bool recursive = false, string filterPattern = "*", int limit = 0, CancellationToken token = default)` | 호스트의 인덱스 검색과 변경 감지를 위해 사용자 지정 폴더를 등록합니다. 열거는 호스트 파일 인덱스만 읽어 스트리밍으로 반환하며, 인덱스가 포함하지 않는 폴더는 빈 시퀀스를 반환하므로 로컬 드라이브, 네트워크 또는 폴더 인덱스가 해당 경로를 포함해야 합니다. 호스트는 파일 시스템을 직접 스캔하지 않습니다. 의도한 비대칭에 유의하세요: `RegisterDirectory`는 기본적으로 재귀로 동작하지만 `EnumerateDirectoryAsync`는 그렇지 않습니다. 감시 알림은 디바운스되며 변경된 디렉터리를 포함할 수 있고, 빈 목록은 더 좁은 범위를 확인할 수 없음을 뜻합니다. |
| **`MemoryMaintenanceService`** | `void RequestTrim()` | 플러그인이 임시 메모리를 많이 사용하는 백그라운드 작업을 마친 후 호스트에 지연된 작업 집합 정리를 요청합니다. 요청은 병합되거나 무시될 수 있으며 사용 중인 캐시는 해제하지 않습니다. |
| **`RecentFilesService`** | `Task<IReadOnlyList<ISearchResult>> GetRecentFilesAsync(IReadOnlyList<string> directories, int limit, int maxAgeMinutes, CancellationToken cancellationToken = default)` | 호스트의 메모리 인덱스를 조회해 지정 폴더들에서의 최근 파일을 집계합니다. |
| **`ExplorerPathService`** | `string? GetLastActivePath()`<br>`IReadOnlyList<string> GetOpenedFolderPaths()` | 탐색기 및 모든 파일 대화상자에서 마지막으로 탐색된 활성 작업 디렉토리 경로와, 그곳에서 현재 열려 있는 폴더들을 조회합니다. |
| **`PluginSettingsService`** | `T GetSetting<T>(string pluginId, string key, T defaultValue)`<br>`void SetSetting(string pluginId, string key, object? value)`<br>`bool IsComponentEnabled(string dllName, string componentType, string componentName)`<br>`void NotifySettingChanged(string pluginId, string key, object? value = null)`<br>`event Action<string, string>? SettingChanged`<br>`event Action<string, string, object?>? SettingChangedWithValue`<br>`event Action? ComponentEnablementChanged` | 플러그인 설정과 호스트가 저장한 컴포넌트별 활성화 상태를 읽고 씁니다. `SettingChanged`는 무엇이 변경되었는지 이름을 알려 주고, `SettingChangedWithValue`는 새 값까지 함께 실으므로 수신 측이 값을 다시 읽을 필요가 없습니다. |
| **`SettingsSearchService`** | `IReadOnlyList<SettingsSearchEntryInfo> GetEntries()`<br>`void Invalidate()` | 호스트가 현재 제공하는 검색 가능한 설정 항목을 조회하고, 동적으로 제공되는 항목이 변경될 때 호스트의 캐시된 스냅샷을 갱신하도록 알립니다. |
| **`SettingsWindowService`** | `bool ShowWindow(string? targetSection = null)`<br>`bool ShowEntry(SettingsSearchEntryInfo? entry)` | 테마가 적용된 설정 창을 표시하거나 검색 가능한 설정 항목으로 직접 이동하도록 호스트에 요청합니다. URI나 다른 프로세스를 실행하지 않습니다. |
| **`SearchRefreshService`** | `void RefreshIfMatches(Func<string, bool> queryMatches)` | 비동기 작업 완료 후 일치하는 활성 검색 결과를 재평가하고 뷰를 갱신하도록 호스트에 알림. |
| **`UserDataService`** | `string? GetUserDataDirectory()`<br>`string? GetSharedDataDirectory()` | 사용자 전용 데이터 폴더(개인 설정용) 및 머신 공용 데이터 폴더(Python/Node 런타임 등) 경로 반환. 두 반환형은 nullable입니다: 호스트가 그런 폴더를 확인할 수 없는 상태일 수 있으므로 경로를 가정하지 말고 `null`을 확인하세요. |
| **`Logger`** | `void Log(string message, LogLevel level = LogLevel.Info)` | `app.log`에 로그를 기록하고 설정 센터의 실시간 로그 뷰어에 동기화. 이 타입은 `Lertaro.PluginSdk.Services`가 아니라 루트 `Lertaro.PluginSdk` 네임스페이스에 있습니다. |
| **`PluginPromptService`** | `IReadOnlyDictionary<string, object?>? Prompt(string title, IReadOnlyList<PluginConfigField> fields, IReadOnlyDictionary<string, object?>? initialValues = null)` | 스키마를 기반으로 자동 렌더링되는 경량 모달 입력 대화상자 표시. 동기식입니다: 제출된 값을 반환하고 사용자가 취소하면 `null`을 반환하므로 `await`하지 마세요. |
| **`PluginNotificationService`** | `INotificationHandle Show(NotificationRequest request)`<br>`Task<NotificationResult> ShowAsync(NotificationRequest request)`<br>`bool Show(string title, string text, Action? onClick = null)` | 호스트 자신의 창으로 배경 알림을 표시합니다. 오른쪽 아래 카드 더미(`NotificationPosition.CardStack`)와 화면 아래 중앙의 한 줄 알림(`BottomNotice`) 중 하나를 고릅니다. 호스트가 해당 위치가 허용하는 범위로 표시 시간을 자르고, 호출한 어셈블리에서 보낸 주체를 적으므로 플러그인 자기 표시를 조작할 수는 없습니다. 독점 전체 화면 앱이 화면을 차지하는 동안에는 카드가 한 줄 알림으로 줄어듭니다. 예외가 플러그인의 백그라운드 스레드로 되돌아가는 일은 없고, 화면에 아무것도 나타나지 않은 경우를 포함해 핸들 대기 작업은 반드시 끝나며, `bool` 오버로드는 호스트가 요청을 받아들였는지만 알려립니다. 답변이 필요하면 `PluginMessageBoxService`를 쓰십시오. 전체 계약 — 표시 시간과 제한, `Id` 기반 대체, 실패 사유, 클릭 의미, 배치와 스레딩 — 은 [**알림**](./notifications)에 정리되어 있습니다. |
| **`PluginMessageBoxService`** | `MessageBoxResult Show(string messageBoxText, string caption = "", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.OK)` | 호스트가 관리하는 메시지 상자를 표시하여 플러그인이 호스트 테마 UI를 사용하도록 하며, 호스트 처리기가 등록되지 않은 경우 시스템 메시지 상자로 대체합니다. |
| **`ExplorerService`** | `void OpenDirectory(string directoryPath, string? fileNameOrFilePath = null)`<br>`void OpenFolder(string? folderPath)` | 지정된 디렉터리를 열거나 파일을 탐색하며, 호스트에 구성된 서드파티 파일 관리자(또는 탐색기 탭)를 따르고 미설정 시 시스템 파일 탐색기로 대체합니다. `OpenFolder`는 "이 폴더를 열거나, 열 대상이 없으면 탐색기로 포커스를 옮기는" 단순 형태이며 `null`을 받아들입니다. |

`SettingsSearchService.GetEntries()`가 반환하는 항목 인덱스는 현재 호스트 프로세스에서만 유효합니다. 항목을 그대로 `SettingsWindowService.ShowEntry(...)`에 전달하면 SDK가 호스트 콜백을 호출하며, `lertaro://` URI를 만들거나 실행하지 않습니다.

`HistoryEntry`는 `Keyword`, `Path`, `Kind`, `Time`(Unix 초), `Count`(항목을 연 횟수)를 제공합니다. `HistoryService.GetHistoryEntries()`는 최근에 연 항목부터 반환합니다.

### 컴포넌트 활성화 상태와 비용이 큰 런타임 상태

`PluginSettingsService.IsComponentEnabled(...)`는 호스트가 관리하는 컴포넌트별 스위치를 읽습니다. 디렉터리 감시기, 백그라운드 작업자, 외부 런타임 또는 기타 비용이 큰 상태를 소유한 컴포넌트는 해당 상태를 초기화하기 전에 스위치를 확인하고, `ComponentEnablementChanged`를 구독하여 사용자가 스위치를 변경할 때 관련 런타임을 시작하거나 중지해야 합니다. 호스트 콜백이 등록되지 않았거나 콜백이 실패하면 이 메서드는 `true`를 반환하므로 완전한 호스트 외부에서도 플러그인을 사용할 수 있습니다.

## 2. Windows Shell 파일 작업 래퍼

`Lertaro.PluginSdk.Shell.FileOperations`는 Windows Shell의 `IFileOperation` COM 인터페이스를 래핑하여 진행률 대화상자, 충돌 안내, `Ctrl+Z` 실행 취소를 네이티브 수준으로 지원합니다:

```csharp
namespace Lertaro.PluginSdk.Shell.FileOperations;

// 여러 파일을 하나의 원자적 Shell 작업으로 일괄 붙여넣기 또는 이동. `move`에는 기본값이 없으니 어느 쪽을 뜻하는지 명시한다.
public static class ShellPasteHelper
{
    public static void PasteAsync(
        IReadOnlyList<string> sourcePaths,
        string destinationFolder,
        bool move,
        Action? onCompleted = null);
}

// 휴지통 이동 또는 영구 삭제. `permanent`에도 기본값이 없다.
public static class ShellDeleteHelper
{
    public static void DeleteAsync(IReadOnlyList<string> paths, bool permanent);
}

// 존재하는 파일 또는 폴더 하나의 이름 변경
public static class ShellRenameHelper
{
    public static void RenameAsync(string path, string newName);
}

// 드래그 앤 드롭 스트림의 가상 파일 추출
public static class VirtualFileExtractor
{
    public static bool HasVirtualFiles(IDataObject? data);                        // FileGroupDescriptorW가 존재하는지
    public static List<string> Extract(IDataObject? data, string targetFolder);   // 동기식; 실제로 써 낸 파일 목록
    public static string? ResolveDestination(string targetFolder, string name);   // 입력이 공백이면 null; 중복 시 (2)로 자동 개명
}
```

세 작업 헬퍼는 모두 `Task`가 아니라 **fire-and-forget `void`**입니다: `PasteAsync`만 선택적인 `onCompleted` 콜백을 받고, 나머지는 아무것도 알려 주지 않습니다. 인수가 비어 있거나 공백뿐이면 예외를 던지지 않고 무시됩니다.

> [!TIP]
> 위 헬퍼는 스스로 **SDK**가 플러그인 자신의 프로세스 안에서 소유하고 시작하는 STA 워커 스레드로 마샬링됩니다(`ShellOperationStaWorker` — SDK 내부 타입이고 플러그인이 등록하거나 설정할 것은 없습니다). 따라서 플러그인이 셸 작업 때문에 COM 아파트먼트 스레딩을 관리할 필요가 없습니다. 취소 기능과 대기할 결과는 의도적으로 제공하지 않습니다. 네이티브 확인·진행률 대화상자가 곧 상호작용이며, 결과를 알아야 하는 플러그인은 작업 이후 대상 폴더를 다시 읽으면 되기 때문입니다.

## 3. 애플리케이션 수명 주기 및 테마 플러그인 창

`AppLifecycleService.RequestRestart()`는 호스트 애플리케이션에 정상적인 재시작을 요청합니다. 호스트가 교체 프로세스를 시작하고 현재 인스턴스가 정상 종료를 마칠 때까지 기다린 뒤 종료하므로 플러그인이 실행 파일을 직접 시작하거나 호스트를 종료할 필요가 없습니다. 호스트가 요청을 수락하면 `true`를 반환합니다.

플러그인 소유 WPF 콘텐츠에는 `Lertaro.PluginSdk.Windows.PluginWindow`가 호스트와 동일한 둥근 모서리 테마 창 프레임을 제공합니다. 플러그인 뷰를 `ContentHostControl.Content`에 지정하고 `Footer`를 통해 하단 버튼을 추가할 수 있습니다. 일반 작업 표시줄 창에는 `PluginWindowMode.Window`, 항상 위에 표시되고 Alt+Tab에서 숨겨지는 대화상자에는 `PluginWindowMode.Dialog`를 사용합니다. 아이콘을 생략하면 호스트의 기본 앱 아이콘이 사용됩니다.

```csharp
var window = new PluginWindow("도구", 720, 470, PluginWindowMode.Dialog);
window.ContentHostControl.Content = new MyView();
window.Footer.Children.Add(new Button { Content = "확인", IsDefault = true });
window.ShowDialog();
```

`PluginWindow.ShowFooter`는 도구에 버튼이 없을 때 하단 행을 숨기며, 아이콘 인수를 생략하면 호스트의 기본 애플리케이션 아이콘을 사용합니다.

## 4. 창, 검색어, 테마 및 미리보기 인프라

| 호스트 서비스 | 주요 메서드 및 시그니처 | 기능 설명 |
| :--- | :--- | :--- |
| **`SearchWindowService`** | `bool IsWindowVisible()`<br>`void ShowWindow(string? query = null)`<br>`void HideWindow()`<br>`void FocusQueryTextBox()` | 플러그인에서 호스트의 검색 창을 조회하고 조작합니다. 시작 검색어를 넘기는 것도 포함됩니다. |
| **`SearchQueryService`** | `void ChangeQuery(string query, bool requery = false)`<br>`string StripQueryTokens(string query)` | 활성 창의 검색어를 기록하고 선택적으로 재검색하며, 호스트의 접미사 토큰을 벗겨 플러그인이 사용자가 입력한 순수 텍스트만 보도록 합니다. |
| **`ThemeService`** | `bool IsDarkTheme` | 자기 콘텐츠를 직접 그리는 플러그인을 위한 플래그 하나. 호스트 델리게이트가 없으면 실행 중인 WPF 애플리케이션의 리소스를 읽으므로, 런처 밖에서 렌더링되는 플러그인도 예외 대신 답을 받습니다. |
| **`PluginPreviewCache`** | `string Register(string title, string pluginName, Lazy<UserControl> factory, Func<object?>? iconProvider = null)`<br>`PluginPreviewEntry? GetEntry(string key)`<br>`UIElement? GetPreview(string key)` | 플러그인 소유 미리보기 컨트롤을 지연 등록하고 호스트가 조회할 키를 넘겨, 컨트롤이 로드 시점이 아니라 실제로 처음 표시될 때 만들어지게 합니다. |
| **`PreviewActivationSignal`** | `void Begin()`<br>`void End()`<br>`bool IsActive`<br>`event Action? FocusStolen` / `void NotifyFocusStolen()` | **프로세스 밖** 네이티브 핸들러가 실제로 호스팅되는 동안 켜져 있습니다 — 차가운 시작뿐 아니라 보기 세션 전체 동안 켜져 있고, 렌더링된 콘텐츠를 조작하면 실제 최상위 창이 뜰 수 있기 때문입니다. 중첩 `Begin`/`End` 깊이를 세므로 제공자는 호출을 반드시 짝지어야 합니다. *현재 호스트 동작일 뿐 계약이 아닙니다:* 신호가 켜져 있는 동안 검색 창들은 자신의 비활성화를 "사용자가 다른 곳을 클릭한 것"으로 취급하지 않습니다. |
| **`PreviewDialogSignal`** | `void NotifyDialogOpened()`<br>`void NotifyDialogClosed()`<br>`event Action? DialogOpened` / `DialogClosed` | 제공자가 발생시키는 신호로, 네이티브 핸들러 자신의 팝업이 나타날 때 뜹니다. 미리보기 중인 암호화 파일에 대한 Word의 "암호 입력" 요구가 이 멤버가 존재하는 이유입니다. *현재 호스트 동작일 뿐 계약이 아닙니다:* 대화상자에 닿을 수 있도록 대화상자가 떠 있는 동안 퀵 창과 미리보기가 숨겨졌다가 되돌려집니다. |
| **`LocalSendTransferService`** | `void OpenSendWindow(IReadOnlyList<string>? files, string? text)` | 파일 목록이나 텍스트 페이로드를 미리 채운 상태로 호스트의 LocalSend 창을 열어, 플러그인이 UI를 소유하지 않고도 전송을 인계할 수 있게 합니다. |
| **`ToolRunService`** | `Func<string, string, Task<string?>>? RunDopusPathsFunc` (호스트가 대입) | 실제로 응답할 수 있는 프로세스에서 외부 도구를 실행합니다. 호스트의 키보드 후크는 특권 상태로 동작하는데, 승격되지 않은 Directory Opus가 특권 `dopusrt.exe`에 응답할 수는 없고(UIPI가 응답을 막음), 프로세스를 강등하려면 후크 토큰이 갖지 않은 권한이 필요합니다. App은 사용자 자신의 권한 수준에서 실행되므로 후크가 요청을 그곳으로 넘깁니다. 델리게이트를 읽고 `null`은 사용 불가로 취급하세요. 도구는 이미 존재하는 파일을 채우므로 출력 파일은 호출 측이 먼저 만들어야 합니다. |
