# URI 프로토콜 (lertaro://)

Lertaro는 처음 실행될 때 Windows 시스템에 커스텀 프로토콜 **`lertaro://`**를 자동으로 등록합니다. 웹 하이퍼링크, 바탕화면 바로가기, 자동화 스크립트 또는 타사 소프트웨어에서 이 프로토콜을 통해 특정 검색을 호출하거나 설정 페이지로 즉시 이동하고 파일 전송을 시작할 수 있습니다.

## 1. 프로토콜 동작 및 단일 인스턴스 라우팅

- **설정 없이 즉시 사용**: 레지스트리를 수동으로 편집할 필요가 없습니다. 앱 실행 시 자동으로 등록 및 무결성 검증이 수행됩니다.
- **단일 인스턴스 라우팅**: Lertaro가 이미 백그라운드에서 실행 중인 경우 `lertaro://` 링크를 열면 실행 중인 인스턴스로 요청이 즉시 전달되며 중복 프로세스가 생성되지 않습니다. 실행 중이 아니면 메인 앱을 시작하고 요청된 동작을 수행합니다.

## 2. 전체 URI 라우팅 명령어 목록

| URI 명령어 형식 | 기능 설명 및 화면 동작 |
| :--- | :--- |
| `lertaro://` | 퀵 검색 윈도우 활성화 및 표시(`Ctrl` 더블 클릭과 동일). |
| `lertaro://search/[키워드]` | 퀵 윈도우를 열고 지정한 `[키워드]`를 미리 채워 즉시 필터링. |
| `lertaro://fullsearch/[키워드]` | 대형 메인 검색 윈도우를 열고 지정한 `[키워드]` 입력. |
| `lertaro://settings/page/[섹션]` | 설정 창을 열고 지정한 최상위 섹션 탭으로 즉시 전환. |
| `lertaro://settings/entry/[ID]` | 설정 창을 열고 특정 세부 설정 항목으로 점프하여 하이라이트 표시. |
| `lertaro://localsend` | LocalSend 무선 파일 전송 빈 창 열기. |
| `lertaro://localsend/items/[인코딩된 절대 경로...]` | LocalSend를 파일 모드로 열고 하나 이상의 파일/폴더 경로를 사전 등록. |
| `lertaro://localsend/text/[인코딩된 텍스트]` | LocalSend를 텍스트 모드로 열고 지정한 텍스트를 입력 완료 상태로 표시. |

### 설정 섹션 키워드 `[섹션]`

대소문자를 구분하지 않으며 설정 창의 사이드바 메뉴와 일치합니다.

```text
Service      - 서비스 상태
Index        - 인덱싱 설정
General      - 일반 설정
Appearance   - 모양 및 테마
Hotkeys      - 단축키 설정
Plugins      - 플러그인 관리
Favorites    - 즐겨찾기
History      - 검색 기록
QuickPanel   - 퀵 패널
About        - 정보 및 업데이트
```

> [!NOTE]
> `lertaro://settings/entry/[ID]`의 ID는 내장된 [**설정 검색**](./instant-answers#2-키워드-실행-기능-내장-플러그인)에서 동적으로 생성되는 내부 번호입니다. 버전 업데이트 시 변경될 수 있으므로 스크립트 연동 시에는 `lertaro://settings/page/[섹션]` 사용을 권장합니다.

## 3. LocalSend 파라미터 및 인코딩 규칙

LocalSend 관련 URI를 사용할 때 모든 경로나 텍스트는 표준 URL 인코딩을 거쳐야 합니다(예: `:`는 `%3A`, `\`는 `%5C`, 공백은 `%20`).

```text
# 여러 파일 경로 사전 등록
lertaro://localsend/items/C%3A%5CUsers%5Ctestuser%5CDesktop%5Cdoc.pdf/D%3A%5CShared%5Cphotos

# 전송할 텍스트 사전 등록
lertaro://localsend/text/Hello%20from%20Lertaro%21
```

- **보안 요구사항**: 전달되는 모든 파일 경로는 로컬 머신에 실제 존재하는 절대 경로여야 합니다. 데이터가 포함된 링크는 기기 선택 화면만 열며 자동으로 전송을 시작하지 않습니다.

## 4. 외부 연동 실전 예제

### 마크다운 및 지식 베이스 링크

Obsidian, Notion 또는 사내 문서에 직접 링크를 삽입할 수 있습니다.

```markdown
[Lertaro 테마 설정 열기](lertaro://settings/page/Appearance)
[프로젝트 재무제표 검색](lertaro://search/재무제표%202026)
```

### 바탕화면 바로가기 및 배치 스크립트

바탕화면에서 바로가기를 만들고 대상에 다음을 입력합니다.

```cmd
lertaro://fullsearch/D:\Projects\
```

PowerShell 스크립트 호출 예시:

```powershell
Start-Process "lertaro://settings/page/General"
```

## 5. 보안 및 알 수 없는 라우팅 오류 처리

- **무음 안전 처리**: 외부 웹페이지나 스크립트에서 URI를 임의로 호출할 수 있으므로, Lertaro는 모든 수신 URI를 엄격히 검증합니다. 잘못된 구문이나 존재하지 않는 경로는 조용히 무시되어 디버그 로그에만 기록되며 예기치 않은 오류를 유발하지 않습니다.
