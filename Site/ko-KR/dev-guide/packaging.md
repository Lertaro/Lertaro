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
        ├── MyCustomPlugin.dll           (플러그인 메인 어셈블리)
        ├── ThirdParty.Managed.dll       (관리되는 서드파티 라이브러리)
        └── x64/
            └── NativeLibrary.dll        (네이티브 C/C++ DLL)
```

- **의존성 자동 탐색**: Lertaro 로더가 `Assembly.LoadFrom`으로 메인 DLL을 로드하면 .NET 런타임이 동일 폴더 내의 의존 라이브러리를 다른 플러그인 간섭 없이 자동 해석하여 로드합니다.
- **네이티브 바이너리 허용**: 스캔 중 네이티브 DLL(`e_sqlite3.dll` 등)이 발견되면 로더는 `Debug` 레벨로 안전하게 스킵하며 오류를 발생시키지 않습니다.

## 2. 빌드 후 자동 복사 설정 (PostBuild)

`.csproj`에 `PostBuild` 대상을 구성하면 빌드 완료 후 산출물이 Lertaro의 `Plugins/` 디렉토리로 자동 배포됩니다.

```xml
<Target Name="PostBuild" AfterTargets="PostBuildEvent">
  <ItemGroup>
    <PluginOutputFiles Include="$(TargetDir)**\*.*" />
  </ItemGroup>
  <Copy SourceFiles="@(PluginOutputFiles)"
        DestinationFolder="..\..\App\bin\$(Configuration)\net10.0-windows\Plugins\$(TargetName)\%(RecursiveDir)"
        SkipUnchangedFiles="true" />
</Target>
```

## 3. 다국어 리소스 임베딩

플러그인이 [`ITranslationProvider`](./sdk/ui-extensions#itranslationprovider)를 구현하는 경우 번역 JSON 파일을 **임베디드 리소스**로 포함하는 것을 권장합니다.

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Translations\**\*.json" />
</ItemGroup>
```

파일 구조는 `Resources/Translations/{CultureName}/{TypeName}.json`(예: `ko-KR/MyCustomPlugin.json`, `en-US/MyCustomPlugin.json`) 규칙을 따릅니다. 코드에서 `TranslationService.LoadEmbeddedTranslations`를 호출하면 현재 UI 언어에 맞춰 자동 파싱됩니다.

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

Windows에서 저장소 루트의 `make.bat`을 실행하기 전에 .NET SDK와 [64비트 Inno Setup 7](https://jrsoftware.org/isdl.php#v7)을 설치해야 합니다. 스크립트는 x64와 `win-arm64`용 게시 출력을 각각 만들고 `dist/`에 다음 파일을 생성합니다.

- x64: `Lertaro-Setup.exe` 및 `Lertaro-Portable.zip`.
- ARM64: `Lertaro-Setup-arm64.exe` 및 `Lertaro-Portable-arm64.zip`.

ARM64 산출물의 애플리케이션 본체는 네이티브 ARM64입니다. ARM64 설치 프로그램은 호환 가능한 Inno Setup 부트스트래퍼를 사용하고, x64 설치 프로그램은 64비트 Inno Setup 7 셸을 사용합니다. 아키텍처별 페이로드와 파일명 접미사는 `make.bat`, `Installer/installer.iss`, 릴리스 워크플로에서 일치하게 유지해야 합니다.
