# 예제 플러그인

Lertaro 자체에는 두 개의 플러그인이 함께 제공되며, 둘 다 실전에서 유용한 참고 예제입니다 — 두 플러그인
모두 Lertaro 저장소의 `Plugins/` 폴더에 있습니다.

## CoreExtensions — 동작과 셸 컨텍스트 메뉴

`CoreExtensionsPlugin`은 `IPlugin`, `IActionProvider`, `IConfigurable` 세 가지 인터페이스를 동시에
구현합니다.

- **`IActionProvider.GetActions()`** 는 열 개의 내장 `ISearchResultAction`을 반환합니다 — 열기, 탐색기에서
  위치 찾기, 경로 복사, 파일 자체를 복사/잘라내기, 해당 위치에서 명령 프롬프트 열기, 파일/폴더 생성, 그리고
  열기와 명령 프롬프트의 상승된(관리자 권한 실행) 변형까지 포함합니다.
- **`IActionProvider.GetDynamicActionProviders()`** 는 단 하나의 `IDynamicActionProvider` —
  `ShellMenuActionProvider` — 를 반환합니다. 이것이 바로 실제 Windows 마우스 우클릭 메뉴(중첩된 계단식
  하위 메뉴, 예를 들어 "보내기"까지 포함)가 Lertaro 자체의 Actions 메뉴 안에 나타나게 만드는 방식입니다.
  고정된 동작 목록이 아니라 *어떤* 외부의, 동적으로 구성되는 메뉴든 Lertaro 안에 그대로 노출하고 싶다면
  이 패턴을 그대로 참고하면 됩니다.
- **`IConfigurable.GetConfigSchema()`** 는 중첩된 필드 그룹과 `StringList` 필드 타입을 사용하는 설정
  스키마를 보여줍니다 — 여러분의 플러그인이 설정 → 플러그인 구성 대화상자에서 단순한 불리언 목록 이상을
  필요로 한다면 읽어볼 가치가 있습니다.
- 다섯 개의 프로바이더가
  [`IQuickPanelTabProvider`](./sdk/ui-extensions#iquickpaneltabprovider)를 구현하며, 이들이 합쳐 이
  인터페이스의 양 극단을 보여줍니다. `FavoritesTabProvider`와 `HistoryTabProvider`는 메모리에 있는
  목록을 그대로 돌려줍니다 — 둘 다 자체 상태가 없으므로 최소한의 참고 예제입니다.
  `WindowsRecentTabProvider`가 반대쪽 극단으로, 백그라운드 작업에서 디렉터리를 읽고 COM으로 셸 바로
  가기를 해석하되 비싼 작업 **전에** 개수를 먼저 잘라내며, 각 항목의 `Metadata.Modified`를 채워 탭의
  최신순 정렬이 의미를 갖게 합니다.
- `LastDirectoryTabProvider`와 `RecentFilesTabProvider`는 읽어 볼 이유가 조금 다릅니다: 둘 다 자기
  데이터가 아예 없고, [`ExplorerPathService`](./sdk/services)와 `RecentFilesService`를 통해 호스트에
  물어봅니다. 플러그인으로 보여 주고 싶은 것을 Lertaro가 이미 알고 있다면 이 패턴을 따라 하세요.

## PinyinAlias — 중국어 파일명을 위한 병음 별칭

`PinyinAliasProvider`는 `IAliasProvider`와 `ITranslationProvider`를 모두 구현합니다 — 관련이 있는 SDK
역할이라면 플러그인이 자유롭게 결합할 수 있으며, 이 플러그인이 그 좋은 예시입니다.

- **`IAliasProvider.InputRanges`/`OutputRanges`** 는 매직 넘버를 중복해서 적는 대신 `PinyinEngine` 자체의
  테이블 범위로부터 두 알파벳을 그대로 선언합니다(`InputRanges`: CJK 블록, `OutputRanges`: `a`-`z`) — 호스트는
  이 둘을 함께 사용하여 `大长今`을 대상으로 한 `大cj`처럼 리터럴과 병음이 혼합된 쿼리 항목을 지원합니다.
- **`IAliasProvider.CanHandle(text)`** 는 실제 작업을 수행하기 전에 중국어 문자가 있는지 먼저 스캔하므로,
  중국어가 아닌 파일명은 별칭 생성을 완전히 건너뜁니다.
- **`IAliasProvider.GetAliases(text)`** 는 문자별 음절 테이블(각 중국어 문자를 가능한 병음 발음에 매핑)을
  구축한 뒤, 전체 병음 별칭과 이니셜만으로 된 별칭을 함께 생성합니다. 다음자(하나 이상의 유효한 발음을
  가진 문자)가 포함된 파일명의 경우, 흔히 쓰이는 모든 조합에 대해 별칭을 생성합니다 — 병적인 입력에서 조합
  폭발이 일어나지 않도록 32개 조합으로 상한을 두며, 각 대안을 `|`로 이어붙여 검색 엔진이 모든 대안이
  동시에 일치해야 하는 것이 아니라 각각을 하나의 후보로 취급하도록 합니다.
- **`ITranslationProvider`** 는 *같은* 클래스에 구현되어 있으며, 이는 순전히 이 플러그인 자체의 UI 문자열
  (예: 표시 이름)을 `TranslationService.LoadEmbeddedTranslations`를 통해 제공하기 위한 것입니다 — 두
  인터페이스는 목적상 서로 무관하지만, 작고 단일 파일로 이루어진 플러그인이다 보니 우연히 한 타입에 함께
  놓여 있을 뿐입니다.
- `lock`으로 보호되는 `Dictionary<string, Dictionary<string, string>>` 캐시는 호출할 때마다 내장된 번역
  JSON을 다시 파싱하지 않도록 해줍니다 — `GetTranslations`에서 사소하지 않은 작업을 수행하는 모든 플러그인에
  적용되는 표준 패턴입니다.

두 플러그인을 나란히 읽어보는 것이 [Plugin SDK 참조](./sdk/core-search-actions)의 각 구성 요소가 실제로
어떻게 맞물리는지 가장 빠르게 파악하는 방법입니다.
