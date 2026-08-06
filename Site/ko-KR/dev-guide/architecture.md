# 아키텍처

![Lertaro architecture](/architecture.svg)

## 프로세스 분리

Lertaro는 권한 수준과 생명 주기에 따라 의도적으로 격리된 세 개의 별도 프로세스로 실행됩니다.

- **`Lertaro.Service`** — `LocalSystem`으로 실행되는 Windows 서비스입니다. 모든 파일 인덱싱을 담당합니다.
  NTFS/ReFS 드라이브의 USN 저널과 MFT를 읽고, 저널이 없는 다른 로컬 파일 시스템은 직접 순회하고 감시하며,
  네트워크 공유를 스캔하고 캐싱하고, 이름 있는 파이프를 통해 검색 쿼리에 응답합니다. 이를 SYSTEM 수준에서
  실행하면 모든 사용자 계정이 볼 수 있는 원시 볼륨 메타데이터를 읽을 수 있으면서도, 대화형 App 프로세스에
  불필요한 상승된 권한을 부여하지 않아도 됩니다.
- **`Lertaro.App`** — 사용자별, 세션 수준의 WPF 애플리케이션으로, 검색 창, 설정 창, 단축키 처리, Actions/QuickLook
  UI를 담당합니다. Service와는 이름 있는 파이프(`Core.Services`의 `SearchService`/`UsnServicePipeServer`)를 통해
  통신하며 디스크 인덱스에 직접 접근하지 않습니다. 또한 자체적으로 사용자별 파이프(`AppSearchPipeService`)를
  하나 더 호스팅하는데, 이를 통해 `lff` CLI 동반 도구(자세한 내용은 [명령줄 검색](../user-guide/cli) 참고)가
  독립적인 클라이언트 프로세스가 처음부터 다시 구성해야 하는 대신, App이 이미 초기화해 둔 검색 상태 — 로드된
  별칭/플러그인 제공자, 구성된 네트워크 드라이브 인덱스 등 — 를 재사용할 수 있습니다.
- **`Lertaro.Service --hook`** — 저수준의 전역 키보드 훅을 호스팅하는 별도의 작은 프로세스입니다. 이렇게
  분리해 두면 훅이 크래시하거나 포그라운드 앱이 오작동하더라도 메인 App 프로세스까지 함께 죽지 않습니다.
  또한 플러그인의 창 통합 어댑터를 로드하여 해당 호출을 이 프로세스 자체에서 실행합니다 — 자세한 내용은
  아래 [플러그인이 관여하는 지점](#플러그인이-관여하는-지점)을 참고하세요.

## 공유 코어

`Core`는 Service와 App 양쪽에서 참조하는 클래스 라이브러리입니다. 다음을 포함합니다.

- 검색 엔진(`Core/SearchIndex/Fzf/*`) — `fzf` 명령줄 도구의 알고리즘을 본뜬 퍼지 매칭 구현체와, 드라이브 문자
  타겟팅 및 경로 모드 검색을 위한 쿼리 파서(`SearchQueryParser`).
- 런타임 인덱스(`Core/IndexV2/*`) — USN/MFT 읽기로부터 구축된 메모리 매핑 방식의 컬럼형 스냅샷 형식으로,
  마지막 스냅샷 이후의 변경 사항을 담는 인메모리 델타 오버레이를 갖고 있습니다.
- IPC 계약(`SearchRequestMessage`, `SearchResponseBinarySerializer` 등) — App과 Service 양쪽에서 그대로
  공유되어, 두 프로세스가 항상 동일한 와이어 포맷에 합의하도록 합니다.
- `Logger` — 프로세스별 로그 파일(`service.log`, `app.log`, `hook.log`)에 기록하며, 이들은 모두 App의
  설정 → 서비스 상태 로그 뷰어에서 읽을 수 있습니다(단, 모두 쓸 수 있는 것은 아닙니다).

## 플러그인이 관여하는 지점

플러그인은 `PluginSdk`를 참조하는 `.dll` 어셈블리이며 App 프로세스에 의해 로드됩니다(자세한 내용은
[시작하기](./getting-started)와 [패키징 및 배포](./packaging) 참고). Lertaro 자체에도 대표 예시로 두
개의 플러그인이 함께 제공됩니다 — `Lertaro.Plugins.CoreExtensions`(내장 파일 동작과 셸 컨텍스트 메뉴 통합)와
`Lertaro.Plugins.PinyinAlias`(중국어 파일명을 위한 병음 별칭) — 두 플러그인을 둘러본 내용은
[예제 플러그인](./examples)을 참고하세요.

플러그인은 Service와 직접 통신하지 않습니다. 대신 Plugin SDK 참조 문서에 정리된 인터페이스를 통해 App과
상호작용하며, (커스텀 인덱싱 디렉터리가 필요한 경우) 디스크 인덱스와는 `DirectoryIndexerService`를 통해
상호작용합니다. 이 서비스가 대신 Service로 프록시해 줍니다.

창 통합 어댑터는 "App 전용"이라는 원칙의 유일한 예외입니다.
[`IActivePathCollector`, `IFileDialogAdapter`, `IInlineSearchAdapter`](./sdk/system-adapters) 구현체는
Hook 프로세스에도 두 번째로 로드되며, 해당 호출은 App이 아니라 그곳에서 실행됩니다. 이 덕분에 App 자체는
항상 비상승 권한으로 실행됨에도 불구하고, Lertaro가 상승된 권한의 파일 탐색기/파일 대화상자/타사 파일
관리자 창을 조작할 수 있습니다 — Windows는 낮은 권한의 프로세스가 더 높은 권한의 프로세스로 입력을 보내는
것을 차단하므로, 호출은 반드시 대상과 동일한 권한 수준으로 실행 중인 프로세스에서 이루어져야 합니다.
