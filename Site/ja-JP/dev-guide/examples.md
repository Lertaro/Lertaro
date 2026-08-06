# サンプルプラグイン

Lertaro 自身には2つのプラグインが同梱されており、実際の参考例として非常に役立ちます——どちらも
Lertaro リポジトリの `Plugins/` フォルダーにあります。

## CoreExtensions — アクションとシェルのコンテキストメニュー

`CoreExtensionsPlugin` は `IPlugin`、`IActionProvider`、`IConfigurable` の3つのインターフェースを同時に実装しています。

- **`IActionProvider.GetActions()`** は10個の組み込み `ISearchResultAction` を返します——開く、
  Explorer で開く場所を表示、パスのコピー、ファイル自体のコピー/切り取り、その場所でコマンドプロンプトを開く、touch/mkdir、そして開くとコマンドプロンプトの昇格(管理者として実行)バリアントです。
- **`IActionProvider.GetDynamicActionProviders()`** は単一の `IDynamicActionProvider` である
  `ShellMenuActionProvider` を返します。これが、実際の Windows 右クリックメニュー(「送る」のようなネストされたカスケードサブメニューを含む)を Lertaro 自身のアクションメニューの中に表示させている仕組みです。固定のアクションリストではなく、*任意の*外部の、動的に構築されるメニューを Lertaro
  内に表示したい場合に真似すべきパターンです。
- **`IConfigurable.GetConfigSchema()`** は、ネストされたフィールドグループと `StringList` フィールドタイプを使った設定スキーマの例を示しています——自分のプラグインの 設定 → プラグイン の設定ダイアログに、フラットなブール値のリスト以上のものが必要な場合は一読の価値があります。
- 5つのプロバイダーが
  [`IQuickPanelTabProvider`](./sdk/ui-extensions#iquickpaneltabprovider) を実装しており、これらでインターフェースの両極をカバーしています。`FavoritesTabProvider` と `HistoryTabProvider` はメモリ上のリストをそのまま返すだけ — どちらも独自の状態を持たないため、最小限のリファレンス実装です。`WindowsRecentTabProvider` はもう一方の極で、バックグラウンドタスクでディレクトリを読み、COM 経由でシェルのショートカットを解決し、高価な処理の**前に**件数を打ち切り、各項目の `Metadata.Modified` を埋めてタブの新しい順に意味を持たせています。
- `LastDirectoryTabProvider` と `RecentFilesTabProvider` を読む理由は少し違います:どちらも自前のデータをまったく持たず、
  [`ExplorerPathService`](./sdk/services) と `RecentFilesService` を通じてホストに尋ねています。プラグインで見せたいものを Lertaro がすでに知っている場合は、このパターンをまねてください。

## PinyinAlias — 中国語ファイル名向けのピンインエイリアス

`PinyinAliasProvider` は `IAliasProvider` と `ITranslationProvider` の両方を実装しています——プラグインは関連する SDK の役割を自由に組み合わせることができ、これはその良いテンプレートです。

- **`IAliasProvider.InputRanges`/`OutputRanges`** は、`PinyinEngine` 自身のテーブルの境界値から2つのアルファベットをそのまま宣言しています(`InputRanges`:CJK ブロック、`OutputRanges`:`a`-`z`)。マジックナンバーを重複させないためです——ホストはこれら両方を使って、`大长今` に対する `大cj` のような、文字リテラルとピンインを混在させたクエリをサポートします。
- **`IAliasProvider.CanHandle(text)`** は、実際の処理を行う前にまず中国語文字が含まれているかをスキャンするため、中国語以外のファイル名はエイリアス生成を完全にスキップします。
- **`IAliasProvider.GetAliases(text)`** は文字単位の音節テーブル(各漢字が取りうるピンイン読みへのマッピング)を構築し、フルピンインのエイリアスと頭文字のみのエイリアスの両方を生成します。多音字
  (有効な読みが複数ある文字)を含むファイル名については、一般的な組み合わせすべてに対してエイリアスを生成します——病的な入力での組み合わせ爆発を避けるため上限は32通りです——各候補は `|` で連結され、検索エンジンはそれらすべてが同時に一致することを要求するのではなく、それぞれを個別の候補として扱います。
- **`ITranslationProvider`** は*同じ*クラスに実装されていますが、これは純粋にこのプラグイン自身の
  UI 文字列(表示名など)を `TranslationService.LoadEmbeddedTranslations` 経由で提供するためのものです——両インターフェースは目的としては無関係ですが、この小さな単一ファイルのプラグインではたまたま
  1つの型にまとまっています。
- `lock` で保護された `Dictionary<string, Dictionary<string, string>>` のキャッシュにより、呼び出しのたびに埋め込みの翻訳 JSON を再パースすることを避けています——`GetTranslations` の中で軽くない処理を行うプラグインにとっての標準的なパターンです。

両方のプラグインを並べて読むことが、[プラグイン SDK リファレンス](./sdk/core-search-actions)の各パーツが実際にどう組み合わさるかを理解する最も手っ取り早い方法です。
