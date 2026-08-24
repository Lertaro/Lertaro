# 빠른 시작

이 장에서는 Lertaro용 네이티브 C# 플러그인 프로젝트를 처음부터 생성하고, 핵심 인터페이스를 구현하며, 로컬에서 로드 및 디버깅하는 과정을 안내합니다.

## 1. 플러그인 프로젝트 생성

Lertaro 플러그인은 표준 .NET 10 클래스 라이브러리 프로젝트입니다. C# 클래스 라이브러리를 생성하고 `.csproj` 파일을 다음과 같이 구성합니다.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <!-- XAML/WPF 커스텀 UI 컨트롤을 직접 작성할 때만 UseWPF 활성화 -->
    <UseWPF>true</UseWPF>
    <AssemblyName>YourCompany.Plugins.MyCustomPlugin</AssemblyName>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <!-- Lertaro 설치 경로의 Lertaro.PluginSdk.dll 참조 -->
    <Reference Include="Lertaro.PluginSdk">
      <HintPath>..\..\App\Lertaro.PluginSdk.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

> [!TIP]
> 검색 소스, 별칭 엔진, CLI 도구 등 순수 로직형 플러그인은 `<UseWPF>`가 불필요합니다. `PluginSdk.dll`의 `<Private>`을 `false`로 설정하면 SDK 자체가 출력 디렉토리에 중복 복사되는 것을 방지할 수 있습니다.

## 2. 플러그인 진입점 `IPlugin` 구현

각 플러그인 어셈블리에는 메인 진입점 역할을 하는 `IPlugin` 인터페이스 구현 공개 클래스가 반드시 하나 포함되어야 합니다.

```csharp
using Lertaro.PluginSdk;

namespace YourCompany.Plugins.MyCustomPlugin;

public class MyCustomPlugin : IPlugin
{
    public string Name => "My Custom Plugin";
    public string Description => "Lertaro SDK 플러그인 개발 기초를 보여주는 예제입니다.";
}
```

이 클래스 또는 별도의 컴포넌트 클래스에 필요한 SDK 인터페이스를 추가로 구현합니다. 예를 들어 실시간 계산 응답을 제공하려면 `IInstantResultProvider`, 설정 폼을 제공하려면 `IConfigurable`을 구현합니다.

## 3. 배포 및 로드 메커니즘

1. 프로젝트를 빌드하여 `YourCompany.Plugins.MyCustomPlugin.dll`을 생성합니다.
2. 컴파일된 DLL(및 의존하는 서드파티 라이브러리)을 Lertaro 설치 디렉토리 하위의 `Plugins\MyCustomPlugin\` 폴더에 배치합니다.
3. Lertaro를 실행(또는 재시작)하면 App 프로세스가 `Plugins/` 디렉토리를 자동 스캔하여 어셈블리를 로드합니다.
4. **설정 → 플러그인**으로 이동하여 설치된 플러그인 및 컴포넌트 상태를 확인합니다.

## 4. 디버깅 및 로그 출력

플러그인 코드 내에서는 `PluginSdk.Services.Logger`를 사용하여 로깅을 수행하는 것을 권장합니다.

```csharp
using Lertaro.PluginSdk.Services;

Logger.Log("플러그인 초기화가 완료되었으며 서비스가 등록되었습니다.", LogLevel.Info);
```

- 출력된 로그는 **설정 → 서비스 상태 → App 탭**에 실시간으로 표시됩니다.
- 로그 레벨(Error / Warn / Info / Debug) 필터링 및 키워드 검색을 지원하여 개발 중 문제를 손쉽게 추적할 수 있습니다.
