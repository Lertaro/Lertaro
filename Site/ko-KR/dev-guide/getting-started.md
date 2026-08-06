# 시작하기

## 플러그인 프로젝트 스캐폴딩

플러그인은 호스트 앱과 동일한 대상 프레임워크(`net10.0-windows`)를 타겟팅하고 `PluginSdk`를 참조하는
평범한 .NET 클래스 라이브러리입니다.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>
    <AssemblyName>YourCompany.Plugins.YourPlugin</AssemblyName>
    <Version>1.0.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <!-- Reference Lertaro.PluginSdk.dll from your Lertaro install directory, or PluginSdk.csproj
         directly if you're building inside the Lertaro repo itself. -->
    <ProjectReference Include="..\..\PluginSdk\PluginSdk.csproj" />
  </ItemGroup>
</Project>
```

`UseWPF`는 플러그인이 직접 WPF UI(커스텀 미리보기, 테마 리소스 딕셔너리 등)를 렌더링할 때만 필요합니다 —
순수하게 검색 제공자 로직만 있는 플러그인이라면 필요하지 않습니다.

## `IPlugin` 구현하기

모든 플러그인은 `IPlugin`을 구현하는 진입점을 정확히 하나 가집니다.

```csharp
public class YourPlugin : IPlugin
{
    public string Name => "Your Plugin";
}
```

그 다음, 여러분의 플러그인이 실제로 필요로 하는 추가 인터페이스를 구현하면 됩니다 — 전체 목록은
[Plugin SDK 참조](./sdk/core-search-actions)를 참고하세요. 대부분의 실제 플러그인은 `IPlugin`에 더해
한두 개의 인터페이스를 추가로 구현합니다(`CoreExtensionsPlugin`은 `IPlugin`, `IActionProvider`,
`IConfigurable`을 구현합니다. [예제 플러그인](./examples) 참고).

## 로드하기

플러그인을 빌드한 뒤, 출력된 DLL을 Lertaro App의 `Plugins/` 폴더(`Lertaro.App.exe` 옆)에 복사하세요 —
App은 시작 시 해당 폴더를 스캔하여 발견되는 모든 플러그인 어셈블리를 로드합니다. 함께 제공되는 플러그인들이
이 단계를 각자의 빌드 과정에서 어떻게 자동화하는지는 [패키징 및 배포](./packaging)를 참고하세요.

## 디버깅

플러그인 전반에서 (`PluginSdk`의) `Logger.Log(message, level)`을 사용하세요 — 그 출력은 **설정 → 서비스
상태** 아래의 **App** 로그 탭에 나타나며, 레벨별로 필터링하고 키워드로 검색할 수 있다는 점까지 호스트 앱
자체 로그와 완전히 동일합니다.
