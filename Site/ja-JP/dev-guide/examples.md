# 公式プラグインのサンプル解説

`Lertaro.PluginSdk` の各インターフェイスの連携を深く理解するために、公式リポジトリに同梱されている 4 つの代表的なプラグインの実装パターンを解説します。

## 1. CoreExtensions —— アクション、Shell メニュー、クイックパネル

`CoreExtensions` は Lertaro の中心的な拡張機能であり、`IPlugin`、`IActionProvider`、`IConfigurable`、および複数のプロバイダーを実装しています。

### 主な実装ポイント

- **静的アクション（`IActionProvider.GetActions()`）**：開く、エクスプローラーで表示、パスクリップボードコピー、ファイルのコピー/切り取り、コマンドプロンプトで開く、管理者として実行など、10 個の基本アクションを提供。
- **Shell コンテキストメニュー統合（`IDynamicActionProvider`）**：`ShellMenuActionProvider` を介して Windows Shell の COM インターフェイスと連携し、階層化された右クリックメニュー（「送る」、7-Zip、VS Code など）を `Ctrl+O` アクションメニュー内に忠実に描画。
- **スキーマ駆動の設定フォーム（`IConfigurable`）**：グループ化（`Group`）、文字列リスト（`StringList`）、ホットキー登録（`Hotkey`）を含むフォームスキーマを定義し、XAML を書かずに設定センターへ UI を自動生成。
- **多彩なクイックパネルタブ（`IQuickPanelTabProvider`）**：
  - `FavoritesTabProvider` / `HistoryTabProvider`：メモリ上のデータをそのまま結果として返し、ディスク I/O を発生させない最小構成。
  - `WindowsRecentTabProvider`：バックグラウンドで `Recent` フォルダーを巡回し、COM でショートカットのリンク先を解決して `Metadata.Modified` を付与。
  - `LastDirectoryTabProvider` / `RecentFilesTabProvider`：ホストが公開している [`ExplorerPathService`](./sdk/services) や `RecentFilesService` を直接参照。

## 2. PinyinAlias —— 非 ASCII エイリアス変換エンジン

`PinyinAlias` は、中国語ファイル名に対するピンイン全スペルおよび頭文字検索をサポートし、`IAliasProvider` と `ITranslationProvider` を実装しています。

### 主な実装ポイント

- **文字境界の宣言（`InputRanges` / `OutputRanges`）**：入力元を CJK 統合漢字、出力先を英小文字 `a`–`z` と定義。ホストはこの情報をもとに、漢字とアルファベットが混在したクエリを字面一致とピンイン一致に自動分割。
- **事前高速判定（`CanHandle(text)`）**：文字列中に該当文字が含まれるかを事前に走査し、英数字のみの場合は即座に `false` を返して不要な処理をスキップ。
- **多音字の組み合わせ生成（`GetAliases(text)`）**：音節マップを構築し、複数の読みが存在する場合にパイプ記号 `|` で連結した候補群を最大 32 通りまで生成して並列照合。
- **多言語リソースとスレッドセーフなキャッシュ**：`ITranslationProvider` を通じてプラグインの表示名を多言語化し、内部では `lock` 付きディクショナリで JSON をキャッシュして高速化。

## 3. FlowLauncherBridge —— コミュニティプラグインの相互運用

`FlowLauncherBridge` は、外部の Flow Launcher プラグインをネイティブレベルで透過的に動かすための大規模ブリッジプラグインです。

### 主な実装ポイント

- **マルチ言語プロセス間ブリッジ**：C# (.NET)、Python 3.12、Node.js v20 LTS、および `.exe` 形式の Flow プラグインを実行。
- **隔離された自己完結ランタイム**：ユーザーデータフォルダー内に Python / Node.js ランタイムを自動配置し、名前付きパイプによる JSON-RPC 通信を実行。
- **動的設定フォームと WebView2 リッチプレビュー**：外部プラグインの `SettingsTemplate.yaml`/`.json` を `PluginConfigSchema` に動的変換し、辞書や天気などのリッチな HTML プレビューを QuickLook 内に表示。

## 4. FileUnlocker —— ファイル占有解除アクション

`FileUnlocker` は Windows Restart Manager API を使ってファイルの占有を調べ、解放を要求する単機能アクションプラグインの例です。

### 主な実装ポイント

- **単一選択の制約**：既存のファイルを 1 件だけ選択した場合にアクションを提供し、フォルダーや複数選択への曖昧な要求を防止。
- **プロセス情報の表示**：占有中のプロセス名、PID、実行ファイルのパスを表示し、変化するファイル状態に対応する再読み込みを提供。
- **要求による解放**：占有プロセスにファイルの解放を要求し、プロセスがない場合や処理中は解放ボタンを無効化。
- **ホストのウィンドウフレーム**：WPF ビューを SDK のテーマ対応 `PluginWindow` ダイアログに配置し、プラグイン側でテーマ、DPI、タスクバー、Alt+Tab の処理を重複実装しない構成。
