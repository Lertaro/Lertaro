# 패키징 및 배포

이 장에서는 Lertaro 플러그인 어셈블리의 디렉토리 구조 표준, 서드파티 의존성 패키징, 다국어 JSON 리소스 임베딩 및 빌드 자동 배포 구성을 안내합니다.

## 1. 플러그인 디렉토리 구조

Lertaro는 시작 시 앱 루트의 `Plugins\` 폴더를 재귀적으로 스캔합니다. 플러그인 간의 의존성 충돌을 방지하기 위해 플러그인별 전용 하위 폴더를 생성하는 것을 강력히 권장합니다.

```text
Lertaro/
├── Lertaro.App.exe
├── Lertaro.PluginSdk.dll
└── Plugins/
    └── MyCustomPlugin/
        ├── Lertaro.Plugins.MyCustomPlugin.dll  (플러그인 메인 어셈블리)
        ├── ThirdParty.Managed.dll              (관리되는 서드파티 라이브러리)
        └── NativeLibrary.dll                   (네이티브 C/C++ DLL — 하위 폴더 없이 나란히 배치)
```

- **의존성 자동 탐색**: Lertaro 로더가 `Assembly.LoadFrom`으로 메인 DLL을 로드하면 .NET 런타임이 동일 폴더 내의 의존 라이브러리를 다른 플러그인 간섭 없이 자동 해석하여 로드합니다.
- **네이티브 의존성은 `runtimes\<rid>\native`가 아니라 DLL 바로 옆에**: 배포되는 플러그인 프로젝트들은 `<GenerateDependencyFile>false</GenerateDependencyFile>`를 설정하므로 `.deps.json`이 생성되지 않고, 런타임에는 RID 하위 폴더를 해석할 deps 그래프가 없습니다. 따라서 네이티브 라이브러리는 애플리케이션 기본 디렉토리에서 발견 가능해야 하고, 그래서 하위 폴더 없이 나란히 복사해야 합니다. 두 아키텍처를 통합 설치 프로그램 하나가 아니라 별도의 산출물로 게시하는 이유도 이것입니다 — 그 평면 로드 디렉토리를 차지할 수 있는 네이티브 복사본은 하나뿐입니다.
- **네이티브 바이너리 허용**: 어셈블리 스캔이 .NET이 아닌 네이티브 바이너리(`e_sqlite3.dll` 등)를 만나면 로더는 `Debug` 레벨로 기록하고 넘어갈 뿐, 잘못된 `Error`를 제기하지 않습니다.

## 2. 빌드 후 자동 복사 설정 (PostBuild)

`.csproj`에 `PostBuild` MSBuild 대상을 추가하면 빌드가 성공할 때마다 산출물이 Lertaro의 디버그 디렉토리로 배포됩니다. 저장소 안 플러그인 프로젝트들이 실제로 쓰는 방식이며, 여기서는 **하위 폴더 없는 평면** 배치와 **Service를 위한 두 번째 복사**에 주목하세요. 별칭 제공자나 번역 제공자가 UI뿐 아니라 인덱서에도 보이는 것이 바로 이 두 번째 복사 덕분입니다:

```xml
<Target Name="PostBuild" AfterTargets="PostBuildEvent">
  <Copy SourceFiles="$(TargetDir)$(TargetName).dll"
        DestinationFolder="..\..\App\bin\$(Configuration)\net10.0-windows\Plugins\"
        SkipUnchangedFiles="true" />
  <Copy SourceFiles="$(TargetDir)$(TargetName).dll"
        DestinationFolder="..\..\Service\bin\$(Configuration)\net10.0-windows\Plugins\"
        SkipUnchangedFiles="true" />
</Target>
```

플러그인이 로드 시점에 서드파티 관리형 또는 네이티브 의존성을 필요로 한다면, 같은 폴더를 대상으로 하는 나란한 `<Copy>` 항목을 하나 더 추가하세요.

## 3. 다국어 리소스 임베딩

플러그인이 [`ITranslationProvider`](./sdk/ui-extensions)를 구현하는 경우 번역 JSON 파일을 **임베디드 리소스**로 포함하는 것을 권장합니다.

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Translations\**\*.json" />
</ItemGroup>
```

파일은 `Resources/Translations/{culture}/{type}.json` 형태로 정리하되, `{type}`은 `TranslationService.LoadEmbeddedTranslations(assembly, cultureKey, typeName)`에 넘기는 `typeName`입니다. 이 저장소의 모든 플러그인은 고정 파일명 **`Plugin.json`**(`Resources/Translations/zh-CN/Plugin.json`, `Resources/Translations/en-US/Plugin.json`, …)을 사용하고 `"Plugin"`을 넘깁니다. `App.json`은 플러그인 관례가 아니라 CoreExtensions에만 존재하는데, CoreExtensions는 호스트 자신의 UI 문자열까지 함께 제공하기 때문입니다. 컬처 폴더는 앱이 지원하는 7개 로케일을 따릅니다. 요청한 컬처에 대응하는 폴더가 없으면 번역 관리자가 먼저 그 플러그인이 지원하는 첫 언어로 물러나고, 그다음 `en-US`로 물러나며, 그것도 없을 때만 `[key]` 자리 표시자로 떨어집니다.

## 4. 버전 및 메타데이터 정의

`.csproj`에 버전 번호와 설명을 작성합니다.

```xml
<PropertyGroup>
  <Version>1.2.0</Version>
  <AssemblyVersion>1.2.0.0</AssemblyVersion>
  <FileVersion>1.2.0.0</FileVersion>
  <Description>고성능 검색 소스 및 컨텍스트 액션 확장 플러그인.</Description>
</PropertyGroup>
```

이 정보는 **설정 → 플러그인**의 관리 카드에 자동으로 표시됩니다.

## 5. 릴리스 빌드 및 아키텍처별 산출물

Windows에서 저장소 루트의 `make.bat`을 실행하기 전에 .NET SDK와 [64비트 Inno Setup 7](https://jrsoftware.org/isdl.php#v7)을 설치해야 합니다(스크립트가 컴파일러가 실제로 7.x인지 확인합니다). 스크립트는 `:build_arch`를 두 번 호출합니다. `ARCH=x64`(RID 없이 게시, 예전과 똑같이) 한 번, `ARCH=arm64`(`-r win-arm64 --self-contained false`로 크로스 게시) 한 번이며, `dist/`에 생성되는 파일은 네 개입니다.

- `Lertaro-Setup.exe` 및 `Lertaro-Portable.zip`(x64).
- `Lertaro-Setup-arm64.exe` 및 `Lertaro-Portable-arm64.zip`(arm64).

두 아키텍처가 통합 설치 프로그램 하나가 아니라 별도 산출물로 게시되는 이유는 BrowserData 플러그인이 네이티브 라이브러리를 함께 싣기 때문입니다. 그 평면이고 `.deps.json`이 없는 로드 디렉토리에 놓일 수 있는 네이티브 복사본은 하나뿐입니다(스크립트 상단 주석 참조). arm64 설치 프로그램은 `Installer/installer.iss`에서 x64판과 다음이 다릅니다: `ArchitecturesAllowed`(`arm64` 대 `x64compatible`), `ArchitecturesInstallIn64BitMode`, `SetupArchitecture=x64`(x64에만 설정), 함께 번들되는 .NET 데스크톱 런타임 파일과 다운로드 주소, `PublishDir`, 출력 파일명. 각 안에 담긴 페이로드는 자기 아키텍처의 네이티브입니다. 릴리스 워크플로는 정확히 이 네 이름의 해시를 계산하고 업로드하며, `UpdateAssetSelector`는 이름으로 이전 설치본을 업데이트 채널에 대응시키므로 이름을 바꾸면 이미 설치된 사용자가 끊깁니다. 산출물 이름은 `make.bat`, `Installer/installer.iss`, 릴리스 워크플로의 자산 목록과 일치하게 유지하세요.
