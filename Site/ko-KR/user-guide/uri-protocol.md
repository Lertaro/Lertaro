# URI 프로토콜 (lertaro://)

Lertaro는 `lertaro://` 링크의 핸들러로 스스로를 등록합니다 — 별도의 설치 단계 없이, 앱이 처음
실행될 때 자동으로 설정됩니다. 이를 통해 링크를 열 수 있는 모든 것(브라우저, 바로가기, 다른 앱,
스크립트)이 단축키를 통해서만이 아니라 Lertaro의 특정 부분으로 바로 이동할 수 있습니다.

Lertaro가 아직 실행 중이 아니라면, `lertaro://` 링크를 열면 앱을 시작한 뒤 그 링크를 따라갑니다.
이미 실행 중이라면, 실행 중인 인스턴스가 그 링크를 직접 처리합니다 — 절대 두 번째 사본을 시작하지
않습니다.

## 경로

| 링크 | 동작 |
|---|---|
| `lertaro://` | 퀵 검색 창을 활성화합니다 — 단축키로 불러오는 것과 동일합니다. |
| `lertaro://search/[keyword]` | `[keyword]`가 미리 채워진 상태로 퀵 검색 창을 활성화합니다. |
| `lertaro://fullsearch/[keyword]` | `[keyword]`가 미리 채워진 상태로 전체 검색 창을 엽니다. |
| `lertaro://settings/page/[section]` | 설정을 특정 최상위 섹션으로 엽니다. |
| `lertaro://settings/entry/[index]` | 설정을 열고 강조 표시된 상태로 특정 설정 항목 하나로 바로 이동합니다. |
| `lertaro://localsend` | 빈 LocalSend 보내기 창을 엽니다. |
| `lertaro://localsend/items[/encoded-item...]` | 파일/폴더 모드로 전환하고 선택적으로 항목마다 인코딩된 경로 세그먼트 하나를 추가합니다. |
| `lertaro://localsend/text[/encoded-text]` | 텍스트 모드로 전환하고 선택적으로 인코딩된 텍스트를 입력합니다. |

```
lertaro://search/report
lertaro://settings/page/Appearance
```

첫 번째는 이미 "report"로 필터링된 퀵 검색 창을 활성화하고, 두 번째는 설정을 외관 페이지에서 바로
엽니다.

`[section]`은 다음 최상위 사이드바 항목 중 하나와 매칭됩니다: `Service`, `Index`, `General`,
`Appearance`, `Hotkeys`, `Plugins`, `Favorites`, `History`, `QuickPanel`, `About` — 대소문자를
구분하지 않습니다.

`[index]`는 직접 손으로 입력하도록 만들어진 값이 아닙니다 — 이는 선택한 설정에 대해
[설정 검색](./instant-answers)이 자체적으로 생성하는 숫자로, 그 결과 중 하나를 선택하면 정확히 그
행으로 왕복할 수 있게 해줍니다. 재시작 사이에 안정적으로 유지되지 않으므로, 특정 숫자가 그대로
유지될 것이라고 가정하지 마세요.

## LocalSend 링크

각 파일/폴더 경로나 텍스트 값은 하나의 완전한 URL 경로 세그먼트로 인코딩해야 합니다. 여러 항목을 추가하려면 항목마다 인코딩된 세그먼트 하나를 추가합니다. 모든 경로는 이미 존재하는 절대 경로여야 합니다. 예:

```
lertaro://localsend/items/C%3A%5CUsers%5Ctestuser%5CDesktop%5Ca.txt/D%3A%5CShared
lertaro://localsend/text/Hello%20world
```

`lertaro://localsend/items`는 수집 페이지를 파일/폴더 모드로 열고 `lertaro://localsend/text`는 텍스트 모드로 엽니다. 내용이 있는 링크는 장치 선택으로 진행하지만 장치를 자동 선택하거나 전송을 자동 시작하지 않습니다. 보내기 창이 이미 열려 있으면 링크는 아무 작업도 하지 않으며 현재 내용이나 상태도 변경하지 않습니다. LocalSend가 비활성화되어 있으면 대신 LocalSend 설정 페이지를 엽니다. 잘못되거나 너무 긴 내용이 있으면 전체 요청을 무시합니다.

## 인식되지 않는 링크

알려진 경로와 일치하지 않는 것(오타, 지원되지 않는 섹션, `lertaro://` 뒤에 붙은 임의의 문자열)은
조용히 무시됩니다. 어떤 웹사이트나 앱이든 먼저 물어보지 않고 이 프로토콜을 호출할 수 있으므로, 잘못되거나
예상치 못한 링크가 놀라운 일을 해서는 안 됩니다 — 여러분 자신의 문제 해결을 위해 로그에는 남지만, 그
외에는 아무 일도 일어나지 않습니다.
