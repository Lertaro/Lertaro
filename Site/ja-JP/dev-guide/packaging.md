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
        ├── MyCustomPlugin.dll           (プラグイン本体)
        ├── ThirdParty.Managed.dll       (マネージド依存ライブラリ)
        └── x64/
            └── NativeLibrary.dll        (ネイティブ C/C++ DLL)
```

- **依存ライブラリの自動解決**：Lertaro のローダーが `Assembly.LoadFrom` でメイン DLL を読み込むと、.NET ランタイムが同一サブフォルダー内の依存ライブラリを自動的に探して読み込みます。
- **ネイティブファイルの許容**：スキャン中にマネージドアセンブリ以外のファイル（例: `e_sqlite3.dll`）が検出された場合、ローダーは `Debug` レベルで安全にスキップし、不要な `Error` ログを出力しません。

## 2. ビルド後の自動コピー設定（PostBuild）

`.csproj` に `PostBuild` ターゲットを追加すると、ビルド成功時に自動的に Lertaro のデバッグ用 `Plugins/` ディレクトリへ配置できます。

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

## 3. 多言語リソースの埋め込み

プラグインが [`ITranslationProvider`](./sdk/ui-extensions#itranslationprovider) を実装している場合、翻訳用 JSON ファイルを **埋め込みリソース** としてアセンブリ内に含めることを推奨します。

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Translations\**\*.json" />
</ItemGroup>
```

ファイル構成は `Resources/Translations/{CultureName}/{TypeName}.json`（例: `ja-JP/MyCustomPlugin.json`, `en-US/MyCustomPlugin.json`）の配置に従います。コードから `TranslationService.LoadEmbeddedTranslations` を呼び出すことで、現在の UI 言語に応じて自動解決されます。

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

Windows でリポジトリのルートから `make.bat` を実行する前に、.NET SDK と[64 ビット版 Inno Setup 7](https://jrsoftware.org/isdl.php#v7)をインストールしてください。スクリプトは x64 と `win-arm64` の公開用出力を分けて作成し、`dist/` に次のファイルを生成します。

- x64：`Lertaro-Setup.exe` と `Lertaro-Portable.zip`。
- ARM64：`Lertaro-Setup-arm64.exe` と `Lertaro-Portable-arm64.zip`。

ARM64 成果物に含まれるアプリケーション本体はネイティブ ARM64 です。ARM64 インストーラーは互換性のある Inno Setup ブートストラッパーを使用し、x64 インストーラーは 64 ビット版 Inno Setup 7 のシェルを使用します。アーキテクチャ別のペイロードとファイル名サフィックスは、`make.bat`、`Installer/installer.iss`、リリースワークフローで一致させてください。
