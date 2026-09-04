# 開発者ガイド

Lertaro 開発者向けリファレンスマニュアルへようこそ。Lertaro は疎結合なマルチプロセスアーキテクチャと拡張性の高いプラグインエコシステムを採用しており、公式 SDK アセンブリ `Lertaro.PluginSdk` を提供しています。この SDK を参照することで、カスタム検索ソースの追加、コンテキストメニューアクションの拡張、サードパーティ製ファイラーや標準ファイルダイアログとの高度な統合、テーマやファイルプレビューのカスタマイズが可能です。

## 1. アーキテクチャと開発フロー

- **[システムアーキテクチャ設計](./architecture)** —— SYSTEM 権限の Windows サービス、ユーザー権限の WPF UI、キーボードフックプロセスの 3 プロセス分離モデルと名前付きパイプ IPC の詳細。
- **[クイックスタートガイド](./getting-started)** —— クラスライブラリの作成、SDK の参照、`IPlugin` エントリポイントの実装、ローカルデバッグの手順。
- **[パッケージングと配布](./packaging)** —— ディレクトリ構造の仕様、依存ライブラリの同梱、多言語 JSON リソースの埋め込み、PostBuild 自動配置。
- **[公式プラグインのサンプル解説](./examples)** —— オープンソースとして同梱されている `CoreExtensions`、`PinyinAlias`、`FlowLauncherBridge` の設計と実装パターンの詳細解説。

## 2. プラグイン SDK API リファレンス

| SDK カテゴリ | 主要インターフェイス・サービス | 主な機能 |
| :--- | :--- | :--- |
| **[検索コアとアクション](./sdk/core-search-actions)** | `ISearchableItemProvider`<br>`IInstantResultProvider`<br>`IAliasProvider`<br>`IQueryTokenProvider`<br>`ISearchResultAction`<br>`IDynamicActionProvider` | 静的インデックスソース、即時計算クエリ、非 ASCII エイリアス変換エンジン、末尾属性トークンハンドラー、静的・動的コンテキストメニュー。 |
| **[システムとダイアログの統合](./sdk/system-adapters)** | `IActivePathCollector`<br>`IFileDialogAdapter`<br>`IInlineSearchAdapter`<br>`IQuickNavigationProvider` | アクティブなファイラーのパス取得、標準ファイルダイアログのフック、インライン検索バーの埋め込みと双方向選択同期、クイックナビゲーションメニュー。 |
| **[UI とプレビューの拡張](./sdk/ui-extensions)** | `ISidebarFilterProvider`<br>`IResultColumnProvider`<br>`IQuickPanelTabProvider`<br>`IFilePreviewProvider`<br>`IThumbnailProvider`<br>`IThemeProvider`<br>`ITranslationProvider` | サイドバーフィルターカテゴリ、テーブル列の拡張、クイックパネルの動的タブ、QuickLook プレビュー描画、サムネイル抽出、WPF テーマ、多言語 i18n。 |
| **[共通データ構造と契約](./sdk/abstractions)** | `ISearchResult`<br>`FileMetadata`<br>`IPluginSearchWindow`<br>`IConfigurable` | 検索結果の読み取り専用モデル、高精度ファイルメタデータ、親ウィンドウの制御ハンドル、スキーマ駆動型ネイティブ設定フォーム。 |
| **[ホストが提供する各種サービス](./sdk/services)** | `FuzzyMatchService`<br>`TranslationService`<br>`IconService`<br>`FavoritesService`<br>`HistoryService`<br>`FileMetadataService`<br>`DirectoryIndexerService`<br>`MemoryMaintenanceService`<br>`RecentFilesService`<br>`ExplorerPathService`<br>`PluginSettingsService`<br>`SettingsSearchService`<br>`SettingsWindowService`<br>`SearchRefreshService`<br>`UserDataService`<br>`Logger` | 高性能ホスト機能：fzf あいまい一致とハイライトマスク、多言語解析、キャッシュ付きアイコン抽出、お気に入り管理・履歴取得、インデックスサービス連携、遅延メモリ整理、データ分離、Shell ファイル操作。 |

> [!NOTE]
> 本マニュアルのすべてのインターフェイス定義、引数、動作仕様は `Lertaro.PluginSdk` のソースコードに基づいて厳密に記述されています。
