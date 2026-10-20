# 문제 해결

Lertaro 사용 중 문제가 발생했나요? 아래 단계에 따라 일반적인 원인을 확인하고 해결하세요.

## 1. 전역 단축키가 작동하지 않음

- **백그라운드 서비스 실행 여부 확인**: [**설정 → 서비스 상태**](./settings/service-status)로 이동하여 백그라운드 인덱싱 서비스가 실행 중인지 확인하세요. 일반 단축키는 App이 처리하지만 관리자 권한 후킹 프로세스는 서비스에 의존합니다.
- **관리자 권한 격리 (UIPI)**: 현재 활성 창이 관리자 권한으로 실행된 경우(예: 관리자 터미널, 작업 관리자) Windows UIPI 보안 정책으로 인해 일반 프로세스의 단축키 입력이 차단됩니다. Lertaro는 서비스를 통해 후킹을 자동 승격하므로 서비스가 정상 동작 중인지 확인하세요.
- **프로세스 블랙리스트 확인**: [**설정 → 단축키**](./settings/hotkeys-page#_3-프로세스-블랙리스트-process-blacklist)에서 현재 프로그램이 블랙리스트에 등록되어 있지 않은지 확인하세요. 블랙리스트에 포함된 앱이 활성화되어 있으면 Lertaro는 단축키를 가로채지 않습니다.
- **전체화면 게임/앱 자동 무시**: 전체화면 3D 게임이나 비디오 재생기가 실행 중일 때는 단축키가 기본적으로 무시됩니다. 전체화면에서도 검색창을 띄우려면 **설정 → 단축키**에서 **전체화면 앱 포커스 시에도 응답**을 켜세요.

## 2. 검색 결과가 갱신되지 않거나 오래됨

- **로컬 NTFS / ReFS 드라이브**: USN 변경 저널을 읽어 거의 지연 없는 밀리초 단위로 실시간 갱신됩니다.
- **FAT32 / exFAT 드라이브**: 파일 시스템 변경 이벤트를 통해 지속적으로 감지합니다.
- **수동 인덱스 재구축**: 예기치 않은 전원 차단 등으로 인덱스 불일치가 발생한 경우, [**설정 → 인덱싱 → 로컬 드라이브**](./settings/index-drives)에서 해당 드라이브의 **인덱스 재구축**을 클릭하세요.

## 3. 네트워크 드라이브가 자동 갱신되지 않음

- **정기 스캔 메커니즘**: 네트워크 공유(SMB / NAS)는 로컬 USN 저널이 없으므로 정기적인 폴링 스캔에 의존합니다.
- **새로고침 모드 확인**: [**설정 → 인덱싱 → 네트워크 드라이브**](./settings/index-drives#_2-네트워크-드라이브-network-drives)에서 해당 드라이브의 새로고침 모드가 "수동"으로 되어 있지 않은지 확인하세요.
- **심볼릭 링크 순환 보호**: NAS 상의 재귀적 심볼릭 링크로 인한 무한 루프를 방지하기 위해 순환 감지 엔진이 자동으로 중복 경로를 건너뜁니다.

## 4. 특정 파일이나 폴더가 검색되지 않음

- **제외 규칙 확인**: [**설정 → 인덱싱 → 제외 규칙**](./settings/index-drives#_5-제외-규칙-exclusions)에서 해당 파일 경로가 정확한 경로, 와일드카드, 정규식에 의해 실수로 제외되지 않았는지 확인하세요.
- **드라이브 활성화 상태 확인**: [**설정 → 인덱싱 → 로컬 드라이브**](./settings/index-drives)에서 해당 드라이브가 활성화되어 있는지 확인하세요.

## 5. 인라인 윈도우에서 IME 한글 후보 창이 뜨지 않음

- **포커스 비점유 설계**: [인라인 윈도우](./getting-started#_3-세-가지-윈도우-모드)는 닫힌 후 원래 프로그램으로 포커스를 깜빡임 없이 돌려주기 위해 포커스를 가로채지 않는 구조입니다. 한글 IME 후보 창은 Windows 정식 포커스를 요구하므로 인라인 모드에서는 나타나지 않을 수 있습니다.
- **권장 해결 방법**:
  1. **병음 직접 입력**: Lertaro에는 병음 별칭 엔진이 내장돼 있어 병음 글자를 치면 중국어 파일명에 문자 도약으로 일치합니다. 입력기 후보 창을 열 필요가 없습니다([**검색 문법**](./search-syntax#_7-다국어-스마트-별칭) 참고).
  2. **퀵 윈도우 사용**: `Ctrl` 더블 클릭으로 열리는 퀵 윈도우는 정식 포커스를 가지므로 모든 IME가 정상 작동합니다.

## 6. 데이터 디렉토리, 권한 및 업데이트 실패

- **데이터 디렉토리 이동**: 설치형 버전의 `%LocalAppData%\Lertaro` 또는 포터블 버전의 `Data\Users`를 심볼릭 링크나 정션으로 다른 파티션으로 옮기는 것은 지원되지만, 링크 대상은 **로컬 고정 볼륨**이어야 합니다. 네트워크 위치나 이동식 볼륨을 가리키는 링크는 시작 시 이유와 함께 거부됩니다([**설정 → 정보**](./settings/about) 참고).
- **시작 거부: 데이터 디렉토리가 다른 ID 소유**: 사용자 데이터 디렉토리의 소유자가 내 계정이 아니면 Lertaro는 시작을 거부하고, 먼저 관리자 권한으로 복구를 시도합니다. 메시지에는 **실제 소유자와 기대하는 SID**가 함께 표시되며, 이것이 진단의 전부입니다. 그 복구의 각 단계(시간 초과로 종료된 경우 멈춰 있던 단계)는 `%TEMP%\Lertaro\repair.log`에 기록되므로 문제를 보고할 때 첨부해 주세요.

폴더 소유자가 `BUILTIN\Administrators`인데 권한은 이미 **나**에게 FullControl을 부여하고 있다면 소유자만 되돌리면 됩니다. 아래 스크립트는 `%LOCALAPPDATA%\Lertaro` 범위의 소유권만 변경하며 권한과 파일 내용은 건드리지 않습니다.

```powershell
$sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
$root = Join-Path $env:LOCALAPPDATA 'Lertaro'
Get-Item -LiteralPath $root | ForEach-Object {
    $acl = $_.GetAccessControl('Owner')
    $acl.SetOwner($sid)
    $_.SetAccessControl($acl)
}
Get-ChildItem -LiteralPath $root -Recurse -Force | ForEach-Object {
    $acl = $_.GetAccessControl('Owner')
    $acl.SetOwner($sid)
    $_.SetAccessControl($acl)
}
```

- **지난 무음 업데이트 실패**: 업데이트 프로그램은 결과(상태, 실패 이유, 롤백 디렉토리)를 설치 경로 옆의 `update-result.json`에 기록합니다. Lertaro는 다음 시작 시 그 이유를 한 번만 표시하며 같은 결과를 반복해서 알리지 않습니다. 알림이 계속 나타나면 그 파일을 삭제하거나 새 버전을 수동으로 덮어쓰기 설치하세요.
- **데이터 루트 아래의 링크는 항상 거부**: 링크로 해석되는 것은 데이터 디렉토리 자체의 체인뿐이며, 그 내부의 하위 디렉토리는 링크일 수 없습니다([**설정 → 정보**](./settings/about) 참고).

## 7. 실행 직후 바로 종료됨

- **설치는 끝났지만 창이 나타나지 않음**: Windows 10 22H2(빌드 19045) 이전에서는 하드웨어 CET를 지원하는 CPU(Intel 12세대 이후, AMD Zen 3 이후)를 사용하더라도 해당 시스템이 사용자 모드 CET를 완전히 지원하지 않아, Lertaro 자체 코드가 실행되기 전에 .NET이 프로세스를 중단합니다. 이벤트 뷰어에는 **.NET Runtime** 오류로 *"Your Windows doesn't fully support CET. Please install all available Windows updates"*가 기록되지만, 로깅이 시작되기 전에 끝나므로 Lertaro 로그는 남지 않습니다.
- **설치 프로그램은 자동으로 처리합니다**: 설치 마법사가 Windows 빌드 번호를 확인해 Windows 10 22H2 이전에서 설치 폴더의 세 실행 파일에 대해 사용자 모드 섀도 스택 정책을 해제합니다. 다른 프로그램에는 영향이 없으며, 설치 마법사를 다시 실행하면 같은 설정이 다시 적용됩니다.
- **포터블 버전**: 설치 마법사의 대상이 아니므로 PowerShell을 관리자 권한으로 연 뒤 경로를 실제 폴더로 바꿔 다음 세 명령을 한 번 실행하세요.

```powershell
Set-ProcessMitigation -Name 'D:\Lertaro\Lertaro.App.exe' -Disable UserShadowStack
Set-ProcessMitigation -Name 'D:\Lertaro\Lertaro.Service.exe' -Disable UserShadowStack
Set-ProcessMitigation -Name 'D:\Lertaro\lff.exe' -Disable UserShadowStack
```

- **위 .NET 오류가 안내하는 대로 사용 가능한 Windows 업데이트를 모두 설치**하는 것도 다른 해결 방법입니다. 그래도 바로 종료된다면 다음 절의 방법으로 로그를 수집해 Issue를 등록해 주세요.

## 8. 로그 확인 및 이슈 등록

문제가 지속되면 [**설정 → 서비스 상태**](./settings/service-status)에서 실시간 로그를 확인하세요.

- **Service 로그**: 인덱싱, USN 저널, 네트워크 스캔, IPC 통신 기록.
- **App 로그**: UI 렌더링, 플러그인 로드, 단축키 및 설정 변경 기록.
- **Hook 로그**: 전역 키보드 후크 및 마우스 제스처 이벤트 스트림.

검색창으로 키워드를 필터링하거나 심각도(Info / Warn / Error)로 정렬하여 에러를 확인하고 GitHub Issue 등록 시 첨부해 주세요.
