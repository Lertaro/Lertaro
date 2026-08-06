<p align="center">
  <img src="../App/logo.png" alt="Lertaro logo" width="120">
</p>

# ⚡ Lertaro

[English](../README.md) | [简体中文](zh-CN.md) | [繁體中文（香港）](zh-HK.md) | [繁體中文（台灣）](zh-TW.md) | [日本語](ja-JP.md) | 한국어 | [Español](es-ES.md)

Lertaro는 **.NET 10 (WPF)** 기반으로 만들어진 초경량, 고성능, 확장 가능한 Windows용 전역 검색 및 생산성 런처입니다. **Everything**과 **Listary**를 대체하는 현대적인 오픈소스 대안으로, NTFS **USN 저널**과 MFT를 직접 읽어 로컬 드라이브를 인덱싱하여 거의 즉각적이고 리소스 사용량이 적은 검색을 제공합니다.

📖 **[전체 문서, 사용자 매뉴얼 및 개발자 매뉴얼](https://lertaro.github.io/ko-KR/)**

## 주요 기능

- **밀리초 단위 인덱싱** —— 디렉터리를 순회하는 대신 NTFS USN 저널/MFT를 직접 읽습니다. 가벼운 백그라운드 서비스가 실시간으로 인덱스를 동기화합니다.
- **FZF 스타일 퍼지 검색** —— 접두사/접미사/정확히 일치/제외 연산자를 지원하는 다중 키워드 퍼지 매칭에, 중국어 파일명을 위한 병음 별칭까지 지원합니다.
- **세 가지 검색 방식** —— 빠른 팝업 창, 전체 메인 창, 그리고 파일 탐색기나 기본 파일 대화 상자에 직접 도킹되는 인라인 검색 바.
- **QuickLook 미리보기**, 우클릭 메뉴 스타일의 동작 메뉴, 모두 재바인딩 가능한 단축키.
- **개방형 플러그인 SDK** —— 검색 제공자, 별칭, 컨텍스트 메뉴 동작, 결과 열, 미리보기, 테마를 확장할 수 있습니다.
- **프로세스 격리** —— SYSTEM 수준의 인덱싱 서비스가 사용자별 앱 UI와 별도 프로세스로 분리되어 동작합니다.

검색 구문, 모든 단축키, 모든 설정 항목은 [사용자 매뉴얼](https://lertaro.github.io/ko-KR/user-guide/)을, 아키텍처와 플러그인 SDK 레퍼런스는 [개발자 매뉴얼](https://lertaro.github.io/ko-KR/dev-guide/)을 참고하세요.

## 다운로드

최신 릴리스는 [홈페이지](https://lertaro.github.io/ko-KR/)에서 받거나 아래 링크에서 바로 받을 수 있습니다:

- **x64 버전 (Intel / AMD 프로세서)**
  - [설치 프로그램 (Lertaro-Setup.exe)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup.exe) — 권장, 백그라운드 서비스를 지원합니다.
  - [포터블 버전 (Lertaro-Portable.zip)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable.zip) — 설치 없이 압축을 풀고 바로 실행합니다.
- **ARM64 네이티브 버전 (Snapdragon / Windows on ARM 기기)**
  - [설치 프로그램 (Lertaro-Setup-arm64.exe)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup-arm64.exe) — ARM 기기 권장, 네이티브 고성능 실행.
  - [포터블 버전 (Lertaro-Portable-arm64.zip)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable-arm64.zip) — ARM 네이티브 포터블 버전.

## 소스에서 빌드하기

요구 사항: Windows 10/11, .NET 10 SDK, Visual Studio 2022 또는 JetBrains Rider. 설치 프로그램을 빌드하려면 [Inno Setup](https://jrsoftware.org/isinfo.php)도 필요합니다.

- `build_and_run.bat` —— App/Core/Service/플러그인을 다시 빌드하고 로컬에서 모두 다시 실행합니다.
- `make.bat` —— Release 빌드를 생성하고 `dist/` 폴더에 x64 및 ARM64 설치 프로그램과 포터블 버전을 출력합니다.

전체 아키텍처와 플러그인 SDK는 [개발자 매뉴얼](https://lertaro.github.io/ko-KR/dev-guide/)을 참고하세요.

## 🎁 후원 및 기부

Lertaro가 도움이 되셨다면 후원을 고려해 주셔서 감사합니다!

- **USDT (TRC20)**: `TNDh3husX1trDW2ZPm4ZZYdoCoCRCZQXn5`

## 라이선스

MIT License.
