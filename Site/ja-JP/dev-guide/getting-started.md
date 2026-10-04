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
    <!-- 必須の接頭辞。アセンブリ名が "Lertaro.Plugins." で始まらない DLL は
         ローダーが読み飛ばします（大文字小文字は区別しません） -->
    <AssemblyName>Lertaro.Plugins.MyCustomPlugin</AssemblyName>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <!-- インストール済みの Lertaro アプリケーションフォルダーにある SDK を参照します。
         本リポジトリーの同梱プラグインは代わりに PluginSdk/ への <ProjectReference> を使っています -->
    <Reference Include="Lertaro.PluginSdk">
      <HintPath>C:\Program Files\Lertaro\Lertaro.PluginSdk.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

> [!WARNING]
> `Lertaro.Plugins.` という接頭辞は慣習ではなく硬性のフィルターです。`Plugins\**\*.dll` の再帰スキャンはアセンブリ名を読み取り、この接頭辞で始まらない DLL にはリフレクションすら行わないため、`YourCompany.MyPlugin.dll` という名前のプラグインは通知なく読み込まれません。`AssemblyName` に接頭辞を残し、`<Private>false</Private>` を設定して SDK が出力先にコピーされないようにしてください。

> [!TIP]
> 検索ソース、エイリアスエンジン、コマンドラインツールなどの純粋なロジックプラグインでは `<UseWPF>` は不要です。

## 2. プラグインエントリポイント `IPlugin` の実装

各プラグインアセンブリには、`IPlugin` を実装した公開クラスが少なくとも 1 つ含まれており、ローダーがそれをインスタンス化してプラグインのエントリポイントとします。

```csharp
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.MyCustomPlugin;

public class MyCustomPlugin : IPlugin
{
    public string Name => "My Custom Plugin";
    public string Description => "Lertaro プラグイン開発の基本を示すサンプルプラグインです。";
}
```

リフレクションは DLL 内の **すべての** `IPlugin` 実装を見つけて、それぞれ 1 インスタンスずつ生成します。そのため 2 つ含めればプラグインは 2 つとして登録され、設定カードも 2 枚並びます。エントリが 2 つ本当に必要な場合を除き、1 つだけ置いてください。

このクラスまたは別のコンポーネントクラスに、目的に応じた SDK インターフェイスを追加実装します。例えば、動的計算結果を返すなら `IInstantResultProvider`、設定画面を提供するなら `IConfigurable` を実装します。

## 3. 配置と読み込み

1. プロジェクトをビルドして `Lertaro.Plugins.MyCustomPlugin.dll` を生成します。
2. 生成された DLL（および依存するサードパーティ製ライブラリ）を、Lertaro の App ルート直下にある `Plugins\` フォルダーの中に配置します。プラグインごとに専用サブフォルダーを分けるのが慣習で、スキャンは再帰的（`Plugins\**\*.dll`）なので `Plugins\MyCustomPlugin\` で動作します。本リポジトリーのビルド自動化は DLL を `Plugins\` にフラットに展開しますが、それでも動作します。
3. Lertaro を起動（または再起動）すると、App プロセスが `Plugins\` をスキャンし、接頭辞を持つすべてのアセンブリを読み込みます。
4. **設定 → プラグイン** を開くと、インストール済みリストにプラグインと各コンポーネントが表示されます。

> [!NOTE]
> プラグインが `IAliasProvider` または `ITranslationProvider` を提供するなら、同じ DLL はバックグラウンドの **Service** 側にも必要です。この 2 種類は Service でも読み込まれ、インデックス行に正しいエイリアスとラベルを載せるためです。同梱のプラグイン プロジェクトがビルド出力を `App\bin\...\Plugins\` と `Service\bin\...\Plugins\` の両方にコピーしているのはこのためです。

## 4. デバッグとログ出力

プラグイン内でのログ記録には `Logger` を使ってください（注意：これは最上位の `Lertaro.PluginSdk` 名前空間にあり、`Lertaro.PluginSdk.Services` にはありません）。

```csharp
using Lertaro.PluginSdk;

Logger.Log("プラグインの初期化が完了し、サービスが登録されました。", LogLevel.Info);
```

- 出力されたログは **設定 → サービス状態 → App タブ** にリアルタイムで表示されます。
- ログレベル（Error / Warn / Info / Debug）での絞り込みやキーワード検索に対応しており、開発時の動作確認が行えます。
