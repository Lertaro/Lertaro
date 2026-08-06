# 패키징 및 배포

## 플러그인이 발견되는 방식

App은 시작 시 자신의 `Plugins/` 폴더(`Lertaro.App.exe` 옆) 안에서 발견되는 모든 `.dll`을 로드하며,
`IPlugin`을 구현하는 타입을 찾습니다. 별도의 매니페스트 파일은 없습니다 — 어셈블리 자체와 그 안의 타입들이
구현하는 SDK 인터페이스가 계약의 전부입니다.

## 자체 의존성 배포하기

플러그인에 자체적인 관리형 또는 네이티브 의존성 DLL(데이터베이스 드라이버, 네이티브 상호운용 라이브러리
등)이 필요하다면, App의 `Plugins/` 폴더 바로 아래에 평면적으로 두는 대신 자체 하위 디렉터리에 넣으세요 —
예를 들어 `Plugins/YourPlugin/YourPlugin.dll`과 그 의존성들을 바로 옆에 함께 두는 식입니다. 로더는
`Plugins/`를 재귀적으로 스캔하며, `Assembly.LoadFrom`의 자체 동일 디렉터리 의존성 탐색 기능이 이후 여러분의
의존성을 자동으로 해석해 주므로, 다른 모든 플러그인의 로드 디렉터리에까지 흘러들어가지 않습니다.

스캐너가 도중에 마주치는 .NET이 아닌 파일(예: `e_sqlite3.dll` 같은 네이티브 DLL)은 예상된 상황이므로
`Debug` 레벨로 로그가 남으며, `Error`가 아닙니다 — 로드에 실제로 실패한 진짜 관리형 어셈블리만 `Error`
레벨로 로그가 남습니다.

완전하고 실제적인 예시는 `BrowserData` 플러그인의 `.csproj`를 참고하세요. 이 플러그인은
`Microsoft.Data.Sqlite`와 그 네이티브 `SQLitePCLRaw`/`e_sqlite3.dll` 의존성을 이 방식으로 번들링하며,
어떤 빌드 메커니즘으로 만들어졌든 상관없이 이들을 자체 하위 폴더로 넣어주는 빌드 후/게시 후 타겟을
사용합니다.

## 개발 중 복사 자동화하기

Lertaro 자체에 함께 제공되는 플러그인들(`CoreExtensions`, `PinyinAlias`)은 각자의 `.csproj`에 빌드 후
타겟을 두어 배포를 자동화합니다. 갓 빌드된 DLL을 App 자체의 출력 `Plugins/` 폴더로 바로 복사하여, 다시
빌드하면 다음 실행 시 즉시 반영되도록 합니다.

```xml
<Target Name="PostBuild" AfterTargets="PostBuildEvent">
  <Copy SourceFiles="$(TargetDir)$(TargetName).dll"
        DestinationFolder="..\..\App\bin\$(Configuration)\net10.0-windows\Plugins\"
        SkipUnchangedFiles="true" />
</Target>
```

여러분 자신의 빌드 출력과 Lertaro App 설치 위치가 실제로 어디에 있는지에 맞게 대상 경로를 조정하세요.

## 내장 번역

플러그인이 `ITranslationProvider`를 구현한다면([UI 및 미리보기 확장](./sdk/ui-extensions) 참고), 번역
JSON 파일을 낱개 파일이 아니라 내장 리소스로 배포하여 DLL과 함께 이동하도록 하세요.

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Translations\**\*.json" />
</ItemGroup>
```

`TranslationService.LoadEmbeddedTranslations`([호스트 서비스](./sdk/services) 참고)가 런타임에 문화권
이름으로 어셈블리에서 이를 다시 읽어옵니다.

## 버전 관리

플러그인의 `.csproj`에 `<Version>`을 지정하세요. 이 값은 **설정 → 플러그인** 아래 카드에서 사용자에게
표시되며, 플러그인이 빌드된 대상 `PluginSdk` 버전과 함께 나타납니다 — SDK 표면이 변경되었을 때 호환성을
확인하는 데 유용합니다.
