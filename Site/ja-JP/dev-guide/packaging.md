# パッケージングと配布

この章では、Lertaro プラグインアセンブリのディレクトリ構造、サードパーティ製ライブラリの同梱、多言語 JSON リソースの埋め込み、および自動ビルド配置フローについて解説します。

## 1. プラグインのディレクトリ構造

Lertaro は起動時にアプリケーションルート直下の `Plugins\` フォルダーを再帰的にスキャンします。プラグイン間の依存関係の競合を防ぐため、プラグインごとに専用のサブフォルダーを作成することを推奨します。

```text
Lertaro/
├── Lertaro.App.exe
├── Lertaro.PluginSdk.dll
└── Plugins/
    └── MyCustomPlugin/
        ├── Lertaro.Plugins.MyCustomPlugin.dll   (プラグイン本体)
        ├── ThirdParty.Managed.dll              (マネージド依存ライブラリ)
        └── NativeLibrary.dll                   (ネイティブ C/C++ 依存ライブラリ —— フラット配置)
```

- **依存ライブラリの自動解決**：Lertaro のローダーが `Assembly.LoadFrom` でメイン DLL を読み込むと、.NET ランタイムが同一サブフォルダー内の依存ライブラリを自動的に探して読み込みます。
- **ネイティブ依存は DLL と並べ、`runtimes\<rid>\native` には置かない**：同梱のプラグイン プロジェクトは `<GenerateDependencyFile>false</GenerateDependencyFile>` を設定しているため `.deps.json` は生成されず、ランタイムには RID サブフォルダーを解決するための deps グラフがありません。したがってネイティブ ライブラリはアプリケーションのベースディレクトリで見付かる必要があり、フラットにコピーします。2 つのアーキテクチャを単一のインストーラーではなく別々の成果物として公開しているのも同じ理由で、フラットな読み込みディレクトリを占められるのは一方のアーキテクチャのネイティブコピーだけだからです。
- **ネイティブファイルの許容**：アセンブリのスキャン中にマネージドアセンブリ以外のネイティブバイナリ（例: `e_sqlite3.dll`）に遭遇した場合、ローダーは `Debug` レベルでログ出力して先へ進み、誤検知の `Error` を出力しません。

## 2. ビルド後の自動コピー設定（PostBuild）

`.csproj` に `PostBuild` ターゲットを追加すると、ビルド成功のたびに出力を Lertaro のデバッグ用ディレクトリへ配置できます。以下は本リポジトリーのプラグイン プロジェクトが実際に行っている内容です。コピー先が **フラット** であること、および **Service 向けの 2 つ目のコピー** に注目してください。この 2 つ目のコピーがあることで、エイリアス プロバイダーや翻訳プロバイダーは UI だけでなくインデクサーからも利用できるようになります。

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

プラグインの読み込み時にサードパーティ製のマネージドまたはネイティブ依存が必要なら、同じフォルダーへのコピーを `<Copy>` アイテムとして並べて追加してください。

## 3. 多言語リソースの埋め込み

プラグインが [`ITranslationProvider`](./sdk/ui-extensions) を実装している場合、翻訳用 JSON ファイルを **埋め込みリソース** としてアセンブリ内に含めることを推奨します。

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Translations\**\*.json" />
</ItemGroup>
```

ファイルは `Resources/Translations/{culture}/{type}.json` の構成で配置します。`{type}` は `TranslationService.LoadEmbeddedTranslations(assembly, cultureKey, typeName)` に渡す `typeName` です。本リポジトリーのすべてのプラグインは固定ファイル名 **`Plugin.json`**（`Resources/Translations/zh-CN/Plugin.json`、`Resources/Translations/en-US/Plugin.json` …）を使い、`"Plugin"` を渡しています。`App.json` はプラグインの慣習ではありません。CoreExtensions にだけ存在します（同プラグインはホスト自身の UI 文字列も供給しています）。カルチャーのフォルダーはアプリが扱う 7 ロケールに従い、フォルダーのないカルチャーは呼び出し元の既定テキストにそのままフォールバックします。

## 4. バージョンとメタデータの指定

`.csproj` にバージョン番号と説明を記述します。

```xml
<PropertyGroup>
  <Version>1.2.0</Version>
  <AssemblyVersion>1.2.0.0</AssemblyVersion>
  <FileVersion>1.2.0.0</FileVersion>
  <Description>高速な検索ソースおよびコンテキストアクション拡張プラグイン。</Description>
</PropertyGroup>
```

これらの情報は **設定 → プラグイン** の管理カードに自動的に表示されます。

## 5. リリースビルドとアーキテクチャ別成果物

Windows でリポジトリのルートから `make.bat` を実行する前に、.NET SDK と[64 ビット版 Inno Setup 7](https://jrsoftware.org/isdl.php#v7)をインストールしてください。現状、スクリプトはビルドルーチンを **x64 について 1 回だけ** 呼び出し、次のファイルを生成します。

- `dist/` に `Lertaro-Setup.exe` と `Lertaro-Portable.zip`。

スクリプト冒頭のコメントは 2 つのアーキテクチャ（"x64 publishes with no RID exactly as it always has; arm64 is a cross-publish"）にふれており、終了時のバナーは arm64 のパスを表示しますが、`ARCH=x64`→`arm64` に切り替える 2 つ目の `:build_arch` 呼び出しは存在しないため、ローカルで実行しても `Lertaro-Setup-arm64.exe` や `Lertaro-Portable-arm64.zip` は作成されません。それでもリリース ワークフローはまさにこれらの名前をハッシュ化してアップロードします。バナーを信じる前にこの点を覚えておいてください。arm64 インストーラーが x64 のものと異なるのは `Installer/installer.iss` 内の `ArchitecturesAllowed`（`arm64` と `x64compatible`）と `SetupArchitecture=x64` だけであり、それぞれの内部にあるペイロードはそのアーキテクチャ向けのネイティブビルドです。成果物名は `make.bat`、`Installer/installer.iss`、リリース ワークフローの資産リストと一致させてください。
