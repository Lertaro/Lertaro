# 공식 플러그인 예제 분석

`Lertaro.PluginSdk`의 여러 인터페이스가 실전에서 어떻게 협력하는지 이해하기 위해 공식 리포지토리에 포함된 4가지 대표 오픈소스 플러그인의 구현 패턴을 분석합니다.

## 1. CoreExtensions —— 액션, Shell 메뉴 및 퀵 패널

`CoreExtensions` 플러그인은 Lertaro의 핵심 기능 확장 패키지로 `IPlugin`, `IActionProvider`, `IConfigurable` 및 여러 하위 프로바이더를 구현합니다.

### 핵심 구현 사항

- **정적 결과 액션 (`IActionProvider.GetActions()`)**: 열기, 탐색기에서 위치 찾기, 전체 경로 복사, 파일 복사/잘라내기, 명령 프롬프트 열기, 관리자 권한 실행 등 10개의 기본 액션 제공.
- **네이티브 Shell 메뉴 통합 (`IDynamicActionProvider`)**: `ShellMenuActionProvider`를 통해 Windows Shell COM 인터페이스와 연동하여 "보내기", 7-Zip, VS Code 등 계단식 우클릭 메뉴를 `Ctrl+O` 액션 메뉴 내에 렌더링.
- **스키마 기반 설정 폼 (`IConfigurable`)**: 그룹화(`Group`), 문자열 목록(`StringList`), 단축키 녹화(`Hotkey`)를 포함한 폼 스키마를 정의하여 XAML 작성 없이 설정 센터에 네이티브 UI 자동 생성.
- **다양한 퀵 패널 탭 (`IQuickPanelTabProvider`)**:
  - `FavoritesTabProvider` / `HistoryTabProvider`: 메모리 상의 목록을 그대로 결과로 반환하는 제로 I/O 최소 구성.
  - `WindowsRecentTabProvider`: 백그라운드에서 `Recent` 폴더를 탐색하고 COM으로 바로가기 대상을 해석하여 `Metadata.Modified`를 채워 정렬 지원.
  - `LastDirectoryTabProvider` / `RecentFilesTabProvider`: 호스트가 제공하는 [`ExplorerPathService`](./sdk/services) 및 `RecentFilesService`를 직접 조회.

## 2. PinyinAlias —— 비 ASCII 별칭 변환 엔진

`PinyinAlias` 플러그인은 중국어 파일명에 대한 병음 전체 및 초성 검색을 지원하며 `IAliasProvider`와 `ITranslationProvider`를 구현합니다.

### 핵심 구현 사항

- **입출력 문자 집합 경계 (`InputRanges` / `OutputRanges`)**: 입력 범위를 CJK 한자 블록, 출력 범위를 소문자 `a`–`z`로 선언. 호스트는 이 경계를 활용해 한자와 영문이 섞인 쿼리를 자면 매칭과 병음 매칭으로 자동 분할.
- **빠른 사전 검사 (`CanHandle(text)`)**: 별칭 생성 전 한자가 포함되어 있는지 먼저 스캔하여 순수 영문 문자열은 즉시 `false`를 반환하고 후속 처리를 건너뜀.
- **다음자 조합 및 별칭 구성 (`GetAliases(text)`)**: 음절 맵을 구성하고 여러 발음이 있는 경우 파이프 기호 `|`로 연결된 후보군을 최대 32개까지 생성하여 병렬 일치 수행.
- **임베디드 다국어 및 스레드 안전 캐싱**: `ITranslationProvider`를 통해 표시 이름을 다국어화하고, 내부에서는 `lock` 기반 딕셔너리로 JSON 번역을 캐싱하여 반복 파싱 방지.

## 3. FlowLauncherBridge —— 커뮤니티 플러그인 호환 및 격리 런타임

`FlowLauncherBridge` 플러그인은 외부 Flow Launcher 생태계를 네이티브급으로 수용하기 위한 대규모 브리지 시스템입니다.

### 핵심 구현 사항

- **다중 언어 프로세스 간 브리지**: C# (.NET), Python 3.12, Node.js v20 LTS 및 `.exe` 플러그인을 원활히 실행.
- **완전 격리 독립 런타임**: 사용자 데이터 디렉토리 내에 Python / Node.js 런타임을 자동 배치하고 명명된 파이프를 통해 JSON-RPC 통신 수행.
- **동적 설정 폼 및 WebView2 리치 미리보기**: 외부 플러그인의 `SettingsTemplate.yaml`/`.json`을 `PluginConfigSchema`로 동적 매핑하고 사전이나 날씨 등의 HTML 카드를 QuickLook 내에 렌더링.

## 4. FileUnlocker —— 파일 점유 해제 액션

`FileUnlocker`는 Windows Restart Manager API로 파일 점유를 조회하고 해제를 요청하는 단일 액션 플러그인의 예입니다.

### 핵심 구현 사항

- **단일 선택 제한**: 존재하는 파일 하나를 선택했을 때만 액션을 제공하여 폴더나 다중 선택에 대한 모호한 요청을 방지.
- **프로세스 정보 표시**: 점유 프로세스 이름, PID, 실행 파일 경로를 표시하고 파일 상태 변화에 대응할 새로 고침을 제공.
- **요청 기반 해제**: 점유 프로세스에 파일 해제를 요청하며, 프로세스가 없거나 작업 중일 때 해제 버튼을 비활성화.
- **호스트 창 프레임**: WPF 뷰를 SDK의 테마 지원 `PluginWindow` 대화상자에 넣어 플러그인이 테마, DPI, 작업 표시줄, Alt+Tab 처리를 중복 구현하지 않도록 구성.
