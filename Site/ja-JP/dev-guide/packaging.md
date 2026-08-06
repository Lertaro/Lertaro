# パッケージングと配布

## プラグインがどのように発見されるか

App は起動時に、自身の `Plugins/` フォルダー(`Lertaro.App.exe` と同じ場所)にある `.dll` をすべて読み込み、`IPlugin` を実装している型を探します。個別のマニフェストファイルはありません——アセンブリ自体と、その中の型がどの SDK インターフェースを実装しているかが、契約のすべてです。

## 自分の依存関係を同梱する

プラグインが独自のマネージドまたはネイティブの依存 DLL(データベースドライバー、ネイティブ相互運用ライブラリなど)を必要とする場合は、それらを `Plugins/` 直下に平置きするのではなく、App の
`Plugins/` フォルダーの下に専用のサブディレクトリを作って配置してください——例えば
`Plugins/YourPlugin/YourPlugin.dll` とその依存関係をすべて同じ場所に置きます。ローダーは
`Plugins/` を再帰的にスキャンし、その後は `Assembly.LoadFrom` 自身の同一ディレクトリ内での依存関係探索が自動的にあなたの依存関係を解決します。他のすべてのプラグインの読み込みディレクトリに影響を及ぼすことはありません。

スキャン中に遭遇する .NET 以外のファイル(`e_sqlite3.dll` のようなネイティブ DLL)は想定内であり、
`Error` ではなく `Debug` レベルでログに記録されます——本当に読み込みに失敗したマネージドアセンブリだけが `Error` でログに記録されます。

完全な実例としては `BrowserData` プラグインの `.csproj` を参照してください。このプラグインは
`Microsoft.Data.Sqlite` とそのネイティブ依存関係である `SQLitePCLRaw`/`e_sqlite3.dll` をこの方法で同梱しており、どのビルド機構で生成されたかに関わらずそれらを自身のサブフォルダーにまとめる
post-build/post-publish ターゲットを備えています。

## 開発中のコピー作業を自動化する

Lertaro 自身に同梱されているプラグイン(`CoreExtensions`、`PinyinAlias`)は、`.csproj` 内の
post-build ターゲットでデプロイを自動化しており、ビルドしたばかりの DLL をそのまま App 自身の出力先の `Plugins/` フォルダーにコピーします。これにより、再ビルド後は次回起動時にすぐに反映されます。

```xml
<Target Name="PostBuild" AfterTargets="PostBuildEvent">
  <Copy SourceFiles="$(TargetDir)$(TargetName).dll"
        DestinationFolder="..\..\App\bin\$(Configuration)\net10.0-windows\Plugins\"
        SkipUnchangedFiles="true" />
</Target>
```

コピー先のパスは、実際の自分のビルド出力先と Lertaro App のインストール場所に合わせて調整してください。

## 埋め込み翻訳

プラグインが `ITranslationProvider` を実装している場合([UI とプレビューの拡張](./sdk/ui-extensions)を参照)、翻訳の JSON ファイルは単独のファイルとしてではなく、埋め込みリソースとして同梱し、DLL と一緒に配布されるようにしてください。

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Translations\**\*.json" />
</ItemGroup>
```

`TranslationService.LoadEmbeddedTranslations`([ホストサービス](./sdk/services)を参照)が、実行時にカルチャ名でこれらをアセンブリから読み戻します。

## バージョン管理

プラグインの `.csproj` に `<Version>` を設定してください。これは **設定 → プラグイン** の下にあるそのプラグインのカードにユーザー向けに表示され、あわせてそのプラグインがビルドされた対象の
`PluginSdk` バージョンも表示されます——SDK の表面が変化したときに互換性を確認するのに役立ちます。
