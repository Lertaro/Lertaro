# クイックスタート

この章では、Lertaro 向けのネイティブ C# プラグインプロジェクトを一から作成し、主要なインターフェイスを実装してローカルで読み込み・デバッグする手順を解説します。

## 1. プラグインプロジェクトの作成

Lertaro プラグインは標準的な .NET 10 クラスライブラリプロジェクトです。C# クラスライブラリを作成し、`.csproj` ファイルを次のように設定します。

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <!-- XAML/WPF のカスタム UI コントロールを直接作成する場合のみ UseWPF を有効化 -->
    <UseWPF>true</UseWPF>
    <AssemblyName>YourCompany.Plugins.MyCustomPlugin</AssemblyName>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <!-- Lertaro のインストール先にある Lertaro.PluginSdk.dll を参照 -->
    <Reference Include="Lertaro.PluginSdk">
      <HintPath>..\..\App\Lertaro.PluginSdk.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

> [!TIP]
> 検索ソース、エイリアスエンジン、コマンドラインツールなどの純粋なロジックプラグインでは `<UseWPF>` は不要です。`PluginSdk.dll` の `<Private>` を `false` に設定することで、SDK 自体が不要に出力先へコピーされるのを防げます。

## 2. プラグインエントリポイント `IPlugin` の実装

各プラグインアセンブリには、メインエントリポイントとして `IPlugin` インターフェイスを実装する公開クラスが必ず 1 つ存在する必要があります。

```csharp
using Lertaro.PluginSdk;

namespace YourCompany.Plugins.MyCustomPlugin;

public class MyCustomPlugin : IPlugin
{
    public string Name => "My Custom Plugin";
    public string Description => "Lertaro プラグイン開発の基本を示すサンプルプラグインです。";
}
```

このクラスまたは別のコンポーネントクラスに、目的に応じた SDK インターフェイスを追加実装します。例えば、動的計算結果を返すなら `IInstantResultProvider`、設定画面を提供するなら `IConfigurable` を実装します。

## 3. 配置と読み込み

1. プロジェクトをビルドして `YourCompany.Plugins.MyCustomPlugin.dll` を生成します。
2. 生成された DLL（および依存するサードパーティ製ライブラリ）を、Lertaro のインストールフォルダー直下にある `Plugins\MyCustomPlugin\` サブフォルダーに配置します。
3. Lertaro を起動（または再起動）すると、App プロセスが `Plugins/` ディレクトリを自動スキャンしてアセンブリを読み込みます。
4. **設定 → プラグイン** を開くと、インストール済みリストにプラグインと各コンポーネントが表示されます。

## 4. デバッグとログ出力

プラグイン内でのログ記録には `PluginSdk.Services.Logger` を使用することを推奨します。

```csharp
using Lertaro.PluginSdk.Services;

Logger.Log("プラグインの初期化が完了し、サービスが登録されました。", LogLevel.Info);
```

- 出力されたログは **設定 → サービス状態 → App タブ** にリアルタイムで表示されます。
- ログレベル（Error / Warn / Info / Debug）での絞り込みやキーワード検索に対応しており、開発時の動作確認がスムーズに行えます。
