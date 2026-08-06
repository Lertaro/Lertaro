# はじめに

## プラグインプロジェクトの雛形を作る

プラグインとは、ホストアプリと同じターゲットフレームワーク(`net10.0-windows`)を対象とし、
`PluginSdk` を参照する、ごく普通の .NET クラスライブラリです。

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

`UseWPF` は、プラグイン自身が何らかの WPF UI(カスタムプレビュー、テーマのリソースディクショナリなど)を描画する場合にのみ必要です——純粋な検索プロバイダーロジックだけのプラグインには不要です。

## `IPlugin` を実装する

すべてのプラグインには、`IPlugin` を実装するエントリポイントがちょうど1つ必要です。

```csharp
public class YourPlugin : IPlugin
{
    public string Name => "Your Plugin";
}
```

そこから先は、プラグインが実際に必要とする追加のインターフェースを実装していきます——全リストは[プラグイン SDK リファレンス](./sdk/core-search-actions)を参照してください。実際の多くのプラグインは `IPlugin` に加えて1つか2つの追加インターフェースを実装します(`CoreExtensionsPlugin` は `IPlugin`、
`IActionProvider`、`IConfigurable` を実装しています。[サンプルプラグイン](./examples)を参照)。

## 読み込ませる

プラグインをビルドし、出力された DLL を `Lertaro.App.exe` と同じ Lertaro App の `Plugins/` フォルダーにコピーしてください——App は起動時にそのフォルダーをスキャンし、見つかったすべてのプラグインアセンブリを読み込みます。同梱のプラグインがこのステップを自身のビルドの一部としてどう自動化しているかについては、[パッケージングと配布](./packaging)を参照してください。

## デバッグ

プラグイン全体で(`PluginSdk` の)`Logger.Log(message, level)` を使用してください——その出力は
**設定 → サービスの状態** の **App** ログタブに表示され、ホストアプリ自身のログとまったく同じようにレベルでフィルタリングしたり、キーワードで検索したりできます。
