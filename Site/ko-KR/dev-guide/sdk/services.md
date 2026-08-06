# 호스트 서비스

호스트 앱의 기능을 다시 플러그인에 노출하는, `PluginSdk.Services`에 있는 정적 서비스들입니다. 각각은
호스트가 시작 시 연결해 두는 델리게이트를 감싸는 얇은 정적 클래스이므로, 플러그인은 그 아래에서 실제로
무엇이 실행되든 항상 동일한 방식으로 호출합니다.

| 서비스 | 목적 |
|---|---|
| `FuzzyMatchService` | `IsMatch(pattern, text)` — `text`(또는 그 별칭 중 하나)가 fzf 문법의 `pattern`과 일치하는지, 호스트 자체 검색이 사용하는 것과 정확히 동일한 매칭 로직으로 판단합니다. `GetHighlightMask(text, query)` — 그 쌍에 대한 문자 단위 하이라이트 마스크로, 호스트 자체 결과가 하이라이트할 때 쓰는 것과 동일한 리터럴/퍼지/별칭 폴백 단계(CJK 병음 포함)를 사용합니다. 이 덕분에 플러그인의 결과도 단순 리터럴 부분 문자열 매칭만 처리하는 대신 일관되게 하이라이트됩니다. |
| `TranslationService` | 현재 활성화된 언어에 대해 런타임에 조회하는 `Get(key)` / `Format(key, args)`. 플러그인 자체의 내장 JSON 번역을 로드하는 `LoadEmbeddedTranslations(assembly, cultureKey, typeName)`. `GetSupportedCultures(assembly)`. `GetCurrentCulture()` — 앱에서 현재 선택된 UI 언어(예: `"zh-CN"`)로, OS 시스템 로캘과는 독립적인 사용자 설정입니다. 원시 문화권 코드 자체가 필요할 때(예: HTTP `Accept-Language` 헤더에 넣거나 번역 API의 대상 언어를 고를 때)만 이를 사용하세요 — `CultureInfo.CurrentUICulture`는 이 설정이 아니라 OS 로캘을 반영하며, 사용자의 Windows 언어와 앱 내 언어가 다를 때는 조용히 이 값과 어긋나게 됩니다. |
| `IconService` | `GetIcon(path, isDir)`와 `GetThumbnail(path, size)` — 캐시된 셸 아이콘/썸네일 추출로, 플러그인이 직접 Windows 아이콘 API를 호출할 필요가 없습니다. |
| `FavoritesService` | `GetFavorites()` — 사용자의 [즐겨찾기](../../user-guide/settings/favorites) 목록(`FavoriteItem`: Name, Path)에 대한 읽기 전용 접근입니다. |
| `HistoryService` | `GetHistoryEntries()` — 기록된 모든 [기록](../../user-guide/settings/history) 항목을 최근에 연 순서로, `HistoryEntry { Keyword, Path, Kind, Time }` 형태로 반환합니다(`Kind`는 `HistoryEntryKind`: `File` / `Folder` / `Application`. `Keyword`는 그 항목으로 이어진 검색 텍스트이며, 검색어 없이 — 예를 들어 빠른 패널 탭에서 — 바로 연 경우에는 빈 문자열입니다. `Time`은 유닉스 초 단위입니다). 각 경로는 가장 최근에 그 경로로 이어진 키워드 아래로 한 번만 나타납니다. |
| `FileMetadataService` | `GetMetadataAsync(paths)` — 현재 결과 목록에 **이미 포함되어 있지 않은** 경로에 대한 일괄 Size/Created/Modified/Accessed 조회([`FileMetadata`](./abstractions#filemetadata)). 모든 `ISearchResult`는 이미 자체 `Metadata` 속성을 통해 이를 무료로 제공하므로([공유 추상화](./abstractions#isearchresult) 참고), 이 서비스는 다른 방식으로 얻은 경로(예: 자체 설정에서 가져온 경로)에 대해서만 사용하세요. |
| `DirectoryIndexerService` | `RegisterDirectory(pluginId, path, recursive, filterPattern)` / `UnregisterDirectories(pluginId)` / `SearchDirectoriesAsync(pluginId, query, token)` — 플러그인이 그 메커니즘을 직접 재구현하지 않고도 자체 디렉터리를 백그라운드 인덱싱과 USN 모니터링에 등록할 수 있게 해줍니다. `WatchDirectories(pluginId, onChanged)`는 **당신이 등록한** 디렉터리가 디스크에서 바뀌었을 때 콜백하며, 구독을 그만두기 위한 `IDisposable`을 돌려줍니다. 브로드캐스트가 아니라 등록자별이라 비교할 id도 없고 남의 변화에 반응할 일도 없습니다 — 그 변화가 누구의 등록에 속하는지는 호스트가 이미 알고 있으니까요. 백그라운드 스레드에서, 그리고 이미 디바운스된 상태로 호출됩니다(대량 복사는 디렉터리가 잠잠해진 뒤 한 번이지 파일마다 한 번이 아닙니다). 호스트가 당신의 디렉터리에 확실히 귀속시킬 수 있는 변화만 알리며, 귀속시킬 수 없을 때(트리 전체를 갈아 끼운 재스캔 등)는 침묵하기보다 호출하는 쪽을 택합니다. `EnumerateDirectoryAsync(path, recursive, filterPattern, limit, token)`은 파일 시스템이 아니라 같은 인덱스에서 한 디렉터리의 내용을 나열합니다 — 호스트가 인덱싱하는 드라이브라면 디스크 I/O가 전혀 없고, 인덱싱되지 않은 디렉터리는 실제 순회로 자동 전환되므로 호출자가 어느 쪽인지 판단할 필요가 없습니다. 스트리밍으로 반환되며 `filterPattern`이 거르는 것은 **파일**입니다(디렉터리는 항상 반환되니 필요 없으면 `IsDir`로 걸러내세요). 숨김/시스템 항목은 절대 반환되지 않습니다. 재귀 나열에서는 `limit`을 설정할 값어치가 있습니다 — `EnumerateDirectoryAsync(@"C:\", recursive: true)`는 요청한 그대로 볼륨의 모든 항목을 돌려줍니다. |
| `RecentFilesService` | `GetRecentFilesAsync(directories, limit, maxAgeMinutes, token)` — 여러 디렉터리 아래에서 가장 새로운 항목들을 최신순으로. 디스크가 아니라 호스트의 메모리 인덱스가 답합니다. 디렉터리별이 아니라 전체를 **하나로** 병합한 목록입니다. 파일만: 폴더 자신의 수정 시각은 안에서 뭔가 추가·삭제될 때마다 바뀌므로, "무엇을 작업했는가"를 보여 줘야 할 목록의 맨 위에 "작업 중인 폴더"가 올라오게 됩니다. `limit`에 0이면 개수 제한 없음, `maxAgeMinutes`에 0이면 기간 제한 없음이지만, 둘 다 두지 않으면 단지 더 새로운 것이 없다는 이유만으로 방치된 폴더가 한 달 전 파일을 계속 내놓습니다. 호스트가 인덱싱하지 않는 디렉터리는 그 자리에서 훑지 않고 그냥 아무것도 기여하지 않습니다 — 여기서 원하는 것은 빠른 답이거나 없거나입니다. 느린 쪽은 `DirectoryIndexerService.EnumerateDirectoryAsync`가 있습니다. |
| `ExplorerPathService` | `GetLastActivePath()` — 탐색기 창이나 파일 대화 상자가 마지막으로 보여 주던 폴더. 한 번도 없었다면 `null`. 호스트 자신의 창 추적이 채우며, Lertaro 자신의 UI뿐 아니라 **모든** 애플리케이션의 파일 대화 상자를 보고 있으므로 "사용자가 마지막으로 실제 들여다본 폴더"를 뜻합니다 — 플러그인이 스스로 알아낼 수 있는 것이 아닙니다. [`IActivePathCollector`](./core-search-actions)와는 반대 방향입니다: 그쪽은 서드파티 파일 관리자가 무엇을 보여 주고 있는지 플러그인이 호스트에게 **알려 주는** 것이고, 이쪽은 물어보는 것입니다. 아직 존재한다는 보장은 없습니다: 기록하는 것은 사용자가 갔던 곳이며, 그 폴더는 이미 삭제되었거나 분리되었을 수 있습니다. |
| `PluginSettingsService` | `GetSetting<T>(pluginId, key, defaultValue)` — 호스트의 설정 저장소에서 플러그인 자체의 영속화된 설정에 대한 읽기 전용 접근입니다. 세 단계를 순서대로 거칩니다. 사용자가 저장한 적이 있다면 그 영속화된 값, 아무것도 저장된 적이 없다면 해당 필드에 대한 `IConfigurable` 스키마 자체의 `DefaultValue`, 그마저도 없다면 마지막 수단으로 여러분이 전달한 `defaultValue` 인수입니다 — 이렇게 하면 스키마에 선언된 기본값이 단일한 정보원이 되어, 호출부에 하드코딩된 사본을 또 둘 필요가 없습니다. 값을 매번 다시 읽는 대신 캐시한다면, `SettingChanged(pluginId, key)` 이벤트를 구독하여 여러분의 플러그인에 대해 이 이벤트가 발생할 때 캐시를 폐기하세요 — 호스트는 설정 페이지에서 저장 직후에 이 이벤트를 발생시키며, 이는 무효화하기에 신뢰할 수 있는 유일한 시점입니다(매 키 입력마다, 또는 폴링 방식으로 확인하면 우연히 다음에 무언가가 트리거될 때까지, 혹은 영영 변경을 감지하지 못할 수 있습니다). |
| `SearchRefreshService` | `RefreshIfMatches(queryMatches)` — 데이터가 비동기로 도착하는 `IInstantResultProvider`용입니다([`IInstantResultProvider`](./core-search-actions#iinstantresultprovider) 참고). 백그라운드 조회가 끝나고 결과를 캐시한 뒤, 검색의 현재 쿼리 텍스트에 대한 서술자와 함께 이를 호출하면, 호스트가 그 서술자에 일치하는 모든 활성 검색을 다시 실행하여 사용자가 다시 입력할 필요 없이 이제 캐시된 결과가 실제로 나타나게 합니다. |
| `Logger` | `Log(message, level = LogLevel.Info)` — App의 로그 파일에 기록하며, **설정 → 서비스 상태 → App**에서 호스트 자체 로그 라인과 완전히 동일하게 보입니다. |
| `PluginPromptService` | `Prompt(title, fields, initialValues?)` — 주어진 [`PluginConfigField`](./abstractions#iconfigurable) 값을 묻는 작은 모달을 표시합니다(`IConfigurable`의 구성 대화상자가 사용하는 것과 동일한 필드 스키마/렌더링). `initialValues`(`Key`로 매칭)나 각 필드 자체의 `DefaultValue`로 미리 채워집니다. 입력된 값을 필드 `Key`로 키가 매겨진 형태로 반환하며, 사용자가 취소했다면 `null`을 반환합니다 — 이 값들은 플러그인의 실제 영속화된 설정에서 읽거나 쓰이는 일이 전혀 없으므로, 실제 설정을 건드리지 않으면서 설정 필드의 스키마를 순전히 일회성 입력(예: "추가하기 전에 이름을 지어주세요")에 재사용해도 안전합니다. |

`LogLevel`은 `Error` / `Warn` / `Info` / `Debug`로,
[서비스 상태 로그 뷰어](../../user-guide/settings/service-status)의 레벨 필터와 일치합니다.

## 셸 파일 작업

`Lertaro.PluginSdk.Shell.FileOperations` — Windows 셸 자체의 `IFileOperation`을 얇게 감싼 것입니다.
플러그인이 파일을 옮길 때 탐색기와 똑같은 진행 대화상자, 똑같은 "파일이 이미 있습니다" 확인, 똑같은
실행 취소 항목이 나옵니다. 미묘하게 다르게 동작하는 `System.IO` 호출이 아니라요.

| 헬퍼 | 용도 |
|---|---|
| `ShellPasteHelper` | `PasteAsync(sourcePaths, destinationFolder, move, onCompleted?)` — 임의 개수의 경로를 한 폴더로 복사(또는 이동)합니다. **하나의** 셸 작업으로 묶이므로 드라이브를 넘나드는 다중 선택도 파일마다가 아니라 대화상자 하나만 뜹니다. 던져 놓고 바로 반환합니다: 사용자가 열어 둔 채 둘 수 있는 네이티브 대화상자를 기다리며 막으면 호출자만 멈출 뿐이니까요. `onCompleted`는 작업이 끝났을 때 발생합니다 — 다 복사했든 사용자가 취소했든 상관없이. 대상 폴더를 보여 주는 화면에게 두 경우의 답은 "다시 가서 보라"로 같기 때문입니다. |
| `ShellDeleteHelper` | `DeleteAsync(paths, permanent)` — 휴지통으로 보내거나 완전히 삭제합니다. 역시 한 번의 작업, 한 번의 확인으로 묶이며 던져 놓고 반환합니다. |
| `VirtualFileExtractor` | `HasVirtualFiles(dataObject)` / `Extract(dataObject, targetFolder)` — 끌기가 실어 온, 아직 디스크에 없는 파일을 써냅니다: 브라우저에서 끌어온 이미지, 메일 클라이언트에서 끌어온 첨부, zip 미리 보기에서 끌어온 파일. 모두 경로가 아니라서 `IDataObject.GetData(DataFormats.FileDrop)`으로는 아무것도 얻지 못합니다. 실제로 오는 것은 이름을 나열한 디스크립터와, 인덱스로 하나씩 건네지는 바이트이고, 이 클래스가 그것을 풀어냅니다. 종류로 거르지 않는 것은 의도한 바입니다: 끌기 쪽이 건네려는 것을 거부하려면 확장자를 믿거나 바이트를 들여다봐야 하는데, 둘 다 이 헬퍼가 할 일이 아닙니다. `ResolveDestination(folder, name)`은 "덮어쓰지 않고 (2)를 붙이는" 같은 이름 규칙으로, 직접 파일을 쓰는 호출자를 위해 공개해 둔 것입니다. |

두 비동기 헬퍼는 SDK 자체의 STA 작업 스레드(`ShellOperationStaWorker`, 호스트가 시작합니다)에서
돌아갑니다 — 셸의 COM 인터페이스가 STA를 요구하므로, 공유하면 플러그인이 자기 아파트먼트를 띄울
필요가 없습니다.
