<p align="center">
  <img src="../App/logo.png" alt="Lertaro logo" width="120">
</p>

# ⚡ Lertaro

[English](../README.md) | [简体中文](zh-CN.md) | [繁體中文（香港）](zh-HK.md) | [繁體中文（台灣）](zh-TW.md) | [日本語](ja-JP.md) | 한국어 | [Español](es-ES.md)

> [!CAUTION]
> **보안 경고: 공식 소스에서만 Lertaro를 다운로드하세요.** 저장소 `github.com/adelmagical742/Lertaro` 및 웹사이트 `adelmagical742.github.io`는 Lertaro를 사칭한 비공식 소스입니다. 해당 주소에서 파일을 다운로드하거나 실행하지 마세요. 유일한 공식 저장소는 [Lertaro/Lertaro](https://github.com/Lertaro/Lertaro)이며, 공식 웹사이트는 [lertaro.github.io](https://lertaro.github.io/), 공식 바이너리는 [GitHub Releases](https://github.com/Lertaro/Lertaro/releases)를 통해서만 배포됩니다.

Lertaro는 **.NET 10 (WPF)** 기반으로 제작된 초경량, 고성능, 확장형 Windows 전역 파일 검색 및 생산성 런처 도구입니다. **Listary**와 **Everything**의 모던한 오픈소스 대안으로서, NTFS **USN 저널** 및 $MFT를 직접 파싱하여 적은 리소스 점유율로 밀리초 단위의 즉각적인 검색을 제공합니다.

📖 **[전체 문서, 사용자 매뉴얼 및 개발자 가이드](https://lertaro.github.io/ko-KR/)**

## 주요 특징

- ⚡ **USN & MFT 저수준 인덱싱** —— 디렉토리를 순회 탐색하지 않고 NTFS/ReFS USN 저널과 $MFT를 직접 읽어 밀리초 단위로 초고속 인덱스를 구성합니다. FAT32/exFAT 변경 감지 및 네트워크 공유 캐시를 지원합니다.
- 🎯 **fzf 스타일 퍼지 매칭 및 별칭** —— 문자 점프 퍼지 일치, 경로 지정 연산자 및 비 ASCII 별칭 변환을 완벽히 지원합니다.
- 📂 **3가지 검색 모드 및 완벽한 창 도킹** —— 퀵 팝업창, 메인 검색 윈도우뿐만 아니라 Windows 표준 파일 열기/저장 대화상자 및 주요 탐색기(파일 탐색기, Total Commander, Directory Opus, OneCommander)에 자동 임베드됩니다.
- 🎬 **액션 메뉴 및 QuickLook 미리보기** —— `Ctrl+O`로 액션 메뉴와 Shell 우클릭 메뉴를 호출하며, `Alt+P`로 QuickLook 파일 즉시 미리보기를 실행합니다.
- 📊 **실시간 디스크 공간 트리맵 분석** —— 기존 인덱스를 바탕으로 실시간 트리맵을 즉시 생성하여 디스크 재스캔 없이 대용량 폴더를 빠르게 정리할 수 있습니다.
- 🧩 **개방형 플러그인 SDK 및 생태계 연동** —— .NET 10 기반의 공식 C# SDK를 제공하며, Flow Launcher 커뮤니티 플러그인 호환 브리지를 내장합니다.
- 🛡️ **3 프로세스 격리 및 오프라인 프라이버시** —— SYSTEM 서비스(`Lertaro.Service`), 사용자 WPF UI(`Lertaro.App`), UIPI 권한 우회 훅 보조 프로세스(`Lertaro.Service --hook`)가 엄격히 분리됩니다. 원격 측정을 일체 전송하지 않습니다.

검색 문법, 단축키, 모든 설정 옵션은 [사용자 매뉴얼](https://lertaro.github.io/ko-KR/user-guide/)에서, 아키텍처와 플러그인 SDK는 [개발자 가이드](https://lertaro.github.io/ko-KR/dev-guide/)에서 확인하세요.

## 다운로드

최신 릴리스는 [공식 홈페이지](https://lertaro.github.io/ko-KR/) 또는 아래 링크에서 직접 다운로드할 수 있습니다:

- **x64 버전 (Intel / AMD 프로세서)**
  - [인스톨러 (Lertaro-Setup.exe)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup.exe) —— 권장, 백그라운드 시스템 서비스 지원.
  - [포터블 버전 (Lertaro-Portable.zip)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable.zip) —— 무설치, 압축 해제 후 즉시 실행.
- **ARM64 네이티브 버전 (Snapdragon / Windows on ARM 기기)**
  - [인스톨러 (Lertaro-Setup-arm64.exe)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup-arm64.exe) —— ARM 기기 권장.
  - [포터블 버전 (Lertaro-Portable-arm64.zip)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable-arm64.zip) —— ARM 네이티브 무설치 포터블 패키지.

## 소스코드에서 빌드하기

요구 사양: Windows 10/11, .NET 10 SDK, Visual Studio 2022 또는 JetBrains Rider. 인스톨러를 제작하려면 [Inno Setup](https://jrsoftware.org/isinfo.php)도 필요합니다.

- `build_and_run.bat` —— App/Core/Service/플러그인을 다시 빌드하고 로컬에서 즉시 재실행합니다.
- `make.bat` —— `dist/` 폴더에 x64 및 ARM64용 Release 인스톨러와 포터블 빌드를 생성합니다.

자세한 아키텍처 설계와 플러그인 SDK는 [개발자 가이드](https://lertaro.github.io/ko-KR/dev-guide/)를 참조하세요.

## 🎁 후원 및 기부

Lertaro가 유용하셨다면 개발 지속을 위한 후원을 부탁드립니다!

- **USDT (TRC20)**: `TNDh3husX1trDW2ZPm4ZZYdoCoCRCZQXn5`

## 라이선스

MIT License에 따라 배포됩니다.
