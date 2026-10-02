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
    <!-- 필수 접두사: 어셈블리 이름이 "Lertaro.Plugins."로 시작하지 않는 DLL은
         로더가(대소문자 구분 없이) 통째로 건너뜁니다 -->
    <AssemblyName>Lertaro.Plugins.MyCustomPlugin</AssemblyName>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <!-- 설치된 Lertaro 애플리케이션 폴더의 SDK를 참조합니다. 이 저장소에서 배포되는
         플러그인들은 대신 PluginSdk/에 대한 <ProjectReference>를 사용합니다 -->
    <Reference Include="Lertaro.PluginSdk">
      <HintPath>C:\Program Files\Lertaro\Lertaro.PluginSdk.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

> [!WARNING]
> `Lertaro.Plugins.` 접두사는 관례가 아니라 강제 필터입니다. 재귀적인 `Plugins\**\*.dll` 스캔은 어셈블리 이름을 읽어, 그 접두어로 시작하지 않는 DLL은 리플렉션조차 하지 않습니다. 따라서 `YourCompany.MyPlugin.dll`이라는 플러그인은 아무 알림 없이 로드되지 않습니다. `AssemblyName`에 접두사를 유지하고 `<Private>false</Private>`를 설정해 SDK가 출력 폴더에 복사되지 않게 하세요.

> [!TIP]
> 검색 소스, 별칭 엔진, CLI 도구 등 순수 로직형 플러그인은 `<UseWPF>`가 불필요합니다.

## 2. 플러그인 진입점 `IPlugin` 구현

각 플러그인 어셈블리에는 로더가 메인 진입점으로 인스턴스화하는 `IPlugin` 인터페이스 구현 공개 클래스가 최소 하나 포함되어야 합니다.

```csharp
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.MyCustomPlugin;

public class MyCustomPlugin : IPlugin
{
    public string Name => "My Custom Plugin";
    public string Description => "Lertaro SDK 플러그인 개발 기초를 보여주는 예제입니다.";
}
```

리플렉션은 DLL 안의 **모든** `IPlugin` 구현을 찾아 각각 인스턴스 하나씩을 만들기 때문에, 두 개를 노출하면 설정 카드 두 개를 가진 플러그인 두 개가 등록됩니다. 항목이 실제로 두 개 필요하지 않다면 하나만 남겨 두세요.

이 클래스 또는 별도의 컴포넌트 클래스에 필요한 SDK 인터페이스를 추가로 구현합니다. 예를 들어 실시간 계산 응답을 제공하려면 `IInstantResultProvider`, 설정 폼을 제공하려면 `IConfigurable`을 구현합니다.

## 3. 배포 및 로드 메커니즘

1. 프로젝트를 빌드하여 `Lertaro.Plugins.MyCustomPlugin.dll`을 생성합니다.
2. 컴파일된 DLL(및 의존하는 서드파티 라이브러리)을 Lertaro App 루트의 `Plugins\` 폴더 아래에 배치합니다. 플러그인별 전용 하위 폴더가 관례이고 스캔은 재귀적이므로(`Plugins\**\*.dll`) `Plugins\MyCustomPlugin\`처럼 넣으면 되고, 저장소 내 빌드 자동화는 DLL을 `Plugins\`에 곧바로 복사해 두는데, 그것도 정상적으로 동작합니다.
3. Lertaro를 실행(또는 재시작)하면 App 프로세스가 `Plugins\`를 스캔하여 이름에 접두사를 담은 어셈블리를 모두 로드합니다.
4. **설정 → 플러그인**으로 이동하여 설치된 플러그인 및 컴포넌트 상태를 확인합니다.

> [!NOTE]
> 플러그인이 `IAliasProvider`나 `ITranslationProvider`를 제공한다면 백그라운드 **Service**에도 같은 DLL이 필요합니다. 이 두 종류는 인덱싱된 행에 올바른 별칭과 레이블이 실리도록 Service에서도 로드되기 때문입니다. 배포되는 플러그인 프로젝트들이 산출물을 `App\bin\...\Plugins\`와 `Service\bin\...\Plugins\` 양쪽에 복사하는 이유가 이것입니다.

## 4. 디버깅 및 로그 출력

플러그인 코드 내에서 애플리케이션 로깅을 할 때는 `Logger`를 사용하세요(참고: 이 타입은 `Lertaro.PluginSdk.Services`가 아니라 루트 `Lertaro.PluginSdk` 네임스페이스에 있습니다).

```csharp
using Lertaro.PluginSdk;

Logger.Log("플러그인 초기화가 완료되었으며 서비스가 등록되었습니다.", LogLevel.Info);
```

- 출력된 로그는 **설정 → 서비스 상태 → App 탭**에 실시간으로 표시됩니다.
- 로그 레벨(Error / Warn / Info / Debug) 필터링 및 키워드 검색을 지원하여 개발 중 문제를 손쉽게 추적할 수 있습니다.
