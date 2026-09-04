# 개발자 가이드

Lertaro 개발자 참조 매뉴얼에 오신 것을 환영합니다. Lertaro는 강력한 결합도 분리 다중 프로세스 아키텍처와 개방형 플러그인 생태계를 갖추고 있으며 공식 SDK 어셈블리 `Lertaro.PluginSdk`를 제공합니다. 개발자는 이 SDK를 참조하여 커스텀 검색 소스를 추가하고, 컨텍스트 액션 메뉴를 확장하며, 타사 파일 관리자 및 네이티브 대화상자와 깊이 연동하거나, 테마 및 파일 미리보기 핸들러를 자유롭게 구현할 수 있습니다.

## 1. 아키텍처 및 개발 워크플로

- **[시스템 아키텍처 설계](./architecture)** —— SYSTEM 권한 Windows 서비스, 사용자 모드 WPF UI, 키보드 후크 프로세스의 3 프로세스 격리 모델과 명명된 파이프 IPC 통신 구조.
- **[빠른 시작 가이드](./getting-started)** —— 클래스 라이브러리 프로젝트 생성, SDK 참조, `IPlugin` 진입점 구현 및 로컬 디버깅 모범 사례.
- **[패키징 및 배포](./packaging)** —— 어셈블리 디렉토리 구조 표준, 서드파티 관리/네이티브 DLL 패키징, 다국어 JSON 리소스 임베딩 및 PostBuild 자동 배포.
- **[공식 플러그인 예제 분석](./examples)** —— 오픈소스로 제공되는 `CoreExtensions`, `PinyinAlias`, `FlowLauncherBridge` 플러그인의 실전 코드 심층 분석.

## 2. 플러그인 SDK API 레퍼런스

| SDK 카테고리 | 핵심 인터페이스 및 서비스 | 주요 기능 설명 |
| :--- | :--- | :--- |
| **[검색 코어 및 액션](./sdk/core-search-actions)** | `ISearchableItemProvider`<br>`IInstantResultProvider`<br>`IAliasProvider`<br>`IQueryTokenProvider`<br>`ISearchResultAction`<br>`IDynamicActionProvider` | 정적 인덱스 소스, 실시간 연산 응답, 비 ASCII 별칭 변환 엔진, 쿼리 접미사 토큰 핸들러 및 정적/동적 컨텍스트 액션 메뉴. |
| **[시스템 및 대화상자 어댑터](./sdk/system-adapters)** | `IActivePathCollector`<br>`IFileDialogAdapter`<br>`IInlineSearchAdapter`<br>`IQuickNavigationProvider` | 활성 파일 탐색기 경로 감지, 네이티브 파일 대화상자 후킹, 인라인 검색바 내장 및 양방향 선택 동기화, 퀵 내비게이션 메뉴. |
| **[UI 및 미리보기 확장](./sdk/ui-extensions)** | `ISidebarFilterProvider`<br>`IResultColumnProvider`<br>`IQuickPanelTabProvider`<br>`IFilePreviewProvider`<br>`IThumbnailProvider`<br>`IThemeProvider`<br>`ITranslationProvider` | 사이드바 필터 카테고리, 테이블 뷰 커스텀 열, 퀵 패널 동적 탭, QuickLook 커스텀 미리보기 렌더러, 썸네일 추출, WPF 테마 리소스 딕셔너리, i18n 언어 팩. |
| **[공유 추상화 계약](./sdk/abstractions)** | `ISearchResult`<br>`FileMetadata`<br>`IPluginSearchWindow`<br>`IConfigurable` | 검색 결과 읽기 전용 데이터 계약, 고정밀 파일 타임스탬프 및 크기 메타데이터, 호스트 윈도우 제어 핸들, 스키마 기반 네이티브 설정 폼. |
| **[호스트 제공 서비스](./sdk/services)** | `FuzzyMatchService`<br>`TranslationService`<br>`IconService`<br>`FavoritesService`<br>`HistoryService`<br>`FileMetadataService`<br>`DirectoryIndexerService`<br>`MemoryMaintenanceService`<br>`RecentFilesService`<br>`ExplorerPathService`<br>`PluginSettingsService`<br>`SettingsSearchService`<br>`SettingsWindowService`<br>`SearchRefreshService`<br>`UserDataService`<br>`Logger` | 고성능 호스트 인프라: fzf 퍼지 매칭 및 하이라이트 마스크, 다국어 파싱, 캐싱 아이콘 추출, 즐겨찾기 관리/기록 조회, 디렉토리 인덱서 프록시, 지연 메모리 정리, 사용자 데이터 격리, Shell 네이티브 파일 작업. |

> [!NOTE]
> 본 매뉴얼의 모든 인터페이스 시그니처, 매개변수 및 동작 계약은 `Lertaro.PluginSdk` 소스 코드를 바탕으로 직접 작성 및 검증되었습니다.
