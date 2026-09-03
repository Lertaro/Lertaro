# 호스트 제공 서비스

`Lertaro.PluginSdk.Services` 네임스페이스는 호스트 내부의 핵심 알고리즘, 캐시, 플랫폼 연동 기능을 플러그인에서 직접 활용할 수 있도록 고성능 정적 서비스를 제공합니다.

## 1. 핵심 정적 서비스 요약

| 서비스명 | 주요 메서드 및 시그니처 | 기능 설명 |
| :--- | :--- | :--- |
| **`FuzzyMatchService`** | `bool IsMatch(string pattern, string text)`<br>`bool[]? GetHighlightMask(string text, string query)`<br>`double GetMatchScore(string text, string query)` | 호스트와 동일한 fzf 퍼지 매칭 엔진을 실행하고 문자 단위 하이라이트 마스크를 계산하며, 일관된 결과 정렬에 사용할 매칭 품질 점수를 제공합니다. |
| **`TranslationService`** | `string Get(string key)`<br>`string Format(string key, params object[] args)`<br>`void LoadEmbeddedTranslations(...)`<br>`string GetCurrentCulture()`<br>`event Action<string>? CultureChanged` | 다국어 동적 파싱 및 런타임 언어 변경 브로드캐스트. `GetCurrentCulture()`는 OS 언어가 아닌 설정 센터에서 선택된 UI 언어 코드(예: `"ko-KR"`)를 반환하며, `CultureChanged`를 구독하여 UI 언어 변경 시 사전 재로드 및 내부 상태를 갱신할 수 있습니다. |
| **`IconService`** | `ImageSource? GetIcon(string path, bool isDir)`<br>`ImageSource? GetThumbnail(string path, int size)` | 메모리 및 디스크 캐시가 적용된 Windows Shell 파일 아이콘 및 썸네일 추출. |
| **`FavoritesService`** | `IReadOnlyList<FavoriteItem> GetFavorites()`<br>`bool IsFavorite(string path)`<br>`bool TryAddFavorite(FavoriteItem favorite)` | 즐겨찾기 목록 조회, 경로의 등록 여부 확인, 호스트 브리지를 통한 즐겨찾기 추가를 제공합니다. |
| **`HistoryService`** | `IReadOnlyList<HistoryEntry> GetHistoryEntries()` | 최근 열어본 순서대로 정렬된 검색 기록(키워드 및 파일 유형 포함) 조회. |
| **`FileMetadataService`** | `Task<IReadOnlyDictionary<string, FileMetadata>> GetMetadataAsync(IEnumerable<string> paths)` | 현재 검색 결과에 포함되지 않은 외부 경로의 파일 크기 및 타임스탬프 일괄 조회. |
| **`DirectoryIndexerService`** | `void RegisterDirectory(string pluginId, string path, bool recursive, string? filterPattern)`<br>`IDisposable WatchDirectories(string pluginId, Action onChanged)`<br>`IAsyncEnumerable<ISearchResult> EnumerateDirectoryAsync(...)` | 백그라운드 서비스에 커스텀 폴더를 등록하여 자동 인덱싱 및 변경 감지; I/O 없는 스트림 열거 지원. |
| **`RecentFilesService`** | `Task<IReadOnlyList<ISearchResult>> GetRecentFilesAsync(IEnumerable<string> directories, int limit, int maxAgeMinutes, CancellationToken token)` | 인메모리 인덱스로부터 지정 폴더 목록의 최근 수정 파일들을 밀리초 단위로 집계 추출. |
| **`ExplorerPathService`** | `string? GetLastActivePath()` | 탐색기 및 모든 파일 대화상자에서 마지막으로 탐색된 활성 작업 디렉토리 경로 조회. |
| **`PluginSettingsService`** | `T GetSetting<T>(string pluginId, string key, T defaultValue)`<br>`bool IsComponentEnabled(string dllName, string componentType, string componentName)`<br>`event Action<string, string>? SettingChanged`<br>`event Action? ComponentEnablementChanged` | 플러그인 설정과 호스트가 저장한 컴포넌트별 활성화 상태를 읽습니다. |
| **`SettingsSearchService`** | `IReadOnlyList<SettingsSearchEntryInfo> GetEntries()`<br>`void Invalidate()` | 호스트가 현재 제공하는 검색 가능한 설정 항목을 조회하고, 동적으로 제공되는 항목이 변경될 때 호스트의 캐시된 스냅샷을 갱신하도록 알립니다. |
| **`SettingsWindowService`** | `bool ShowWindow(string? targetSection = null)`<br>`bool ShowEntry(SettingsSearchEntryInfo? entry)` | 테마가 적용된 설정 창을 표시하거나 검색 가능한 설정 항목으로 직접 이동하도록 호스트에 요청합니다. URI나 다른 프로세스를 실행하지 않습니다. |
| **`SearchRefreshService`** | `void RefreshIfMatches(Func<string, bool> queryMatches)` | 비동기 작업 완료 후 일치하는 활성 검색 결과를 재평가하고 뷰를 갱신하도록 호스트에 알림. |
| **`UserDataService`** | `string GetUserDataDirectory()`<br>`string GetSharedDataDirectory()` | 사용자 전용 데이터 폴더(개인 설정용) 및 머신 공용 데이터 폴더(Python/Node 런타임 등) 경로 반환. |
| **`Logger`** | `void Log(string message, LogLevel level = LogLevel.Info)` | `app.log`에 로그를 기록하고 설정 센터의 실시간 로그 뷰어에 동기화. |
| **`PluginPromptService`** | `Task<Dictionary<string, object?>?> Prompt(string title, IEnumerable<PluginConfigField> fields, ...)` | 스키마를 기반으로 자동 렌더링되는 경량 모달 입력 대화상자 표시. |
| **`PluginMessageBoxService`** | `MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)` | 호스트가 관리하는 메시지 상자를 표시하여 플러그인이 호스트 테마 UI를 사용하도록 하며, 호스트 처리기가 등록되지 않은 경우 시스템 메시지 상자로 대체합니다. |
| **`ExplorerService`** | `void OpenDirectory(string directoryPath, string? fileNameOrFilePath = null)` | 지정된 디렉터리를 열거나 파일을 탐색하며, 호스트에 구성된 서드파티 파일 관리자(또는 탐색기 탭)를 따르고 미설정 시 시스템 파일 탐색기로 대체합니다. |

`SettingsSearchService.GetEntries()`가 반환하는 항목 인덱스는 현재 호스트 프로세스에서만 유효합니다. 항목을 그대로 `SettingsWindowService.ShowEntry(...)`에 전달하면 SDK가 호스트 콜백을 호출하며, `lertaro://` URI를 만들거나 실행하지 않습니다.

### 컴포넌트 활성화 상태와 비용이 큰 런타임 상태

`PluginSettingsService.IsComponentEnabled(...)`는 호스트가 관리하는 컴포넌트별 스위치를 읽습니다. 디렉터리 감시기, 백그라운드 작업자, 외부 런타임 또는 기타 비용이 큰 상태를 소유한 컴포넌트는 해당 상태를 초기화하기 전에 스위치를 확인하고, `ComponentEnablementChanged`를 구독하여 사용자가 스위치를 변경할 때 관련 런타임을 시작하거나 중지해야 합니다. 호스트 콜백이 등록되지 않았거나 콜백이 실패하면 이 메서드는 `true`를 반환하므로 완전한 호스트 외부에서도 플러그인을 사용할 수 있습니다.

## 2. Windows Shell 파일 작업 래퍼

`Lertaro.PluginSdk.Shell.FileOperations`는 Windows Shell의 `IFileOperation` COM 인터페이스를 래핑하여 진행률 대화상자, 충돌 안내, `Ctrl+Z` 실행 취소를 네이티브 수준으로 지원합니다:

```csharp
namespace Lertaro.PluginSdk.Shell.FileOperations;

// 여러 파일 일괄 붙여넣기 또는 이동
public static class ShellPasteHelper
{
    public static void PasteAsync(
        IEnumerable<string> sourcePaths,
        string destinationFolder,
        bool move = false,
        Action? onCompleted = null);
}

// 휴지통 이동 또는 영구 삭제
public static class ShellDeleteHelper
{
    public static void DeleteAsync(IEnumerable<string> paths, bool permanent = false);
}

// 드래그 앤 드롭 가상 파일 스트림 추출
public static class VirtualFileExtractor
{
    public static bool HasVirtualFiles(IDataObject dataObject);
    public static Task<IReadOnlyList<string>> Extract(IDataObject dataObject, string targetFolder);
    public static string ResolveDestination(string folder, string name); // 중복 시 (2) 자동 부여
}
```

> [!TIP]
> 상기 Shell 헬퍼는 SDK 내부의 전용 STA 스레드(`ShellOperationStaWorker`)에서 비동기로 실행되므로 호출 측에서 COM 아파트먼트 스레드를 별도로 생성할 필요가 없습니다.

## 3. 애플리케이션 수명 주기 및 테마 플러그인 창

`AppLifecycleService.RequestRestart()`는 호스트 애플리케이션에 정상적인 재시작을 요청합니다. 호스트가 교체 프로세스를 시작하고 현재 인스턴스가 정상 종료를 마칠 때까지 기다린 뒤 종료하므로 플러그인이 실행 파일을 직접 시작하거나 호스트를 종료할 필요가 없습니다. 호스트가 요청을 수락하면 `true`를 반환합니다.

플러그인 소유 WPF 콘텐츠에는 `Lertaro.PluginSdk.Windows.PluginWindow`가 호스트와 동일한 둥근 모서리 테마 창 프레임을 제공합니다. 플러그인 뷰를 `ContentHostControl.Content`에 지정하고 `Footer`를 통해 하단 버튼을 추가할 수 있습니다. 일반 작업 표시줄 창에는 `PluginWindowMode.Window`, 항상 위에 표시되고 Alt+Tab에서 숨겨지는 대화상자에는 `PluginWindowMode.Dialog`를 사용합니다. 아이콘을 생략하면 호스트의 기본 앱 아이콘이 사용됩니다.

```csharp
var window = new PluginWindow("도구", 720, 470, PluginWindowMode.Dialog);
window.ContentHostControl.Content = new MyView();
window.Footer.Children.Add(new Button { Content = "확인", IsDefault = true });
window.ShowDialog();
```
