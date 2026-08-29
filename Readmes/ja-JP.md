<p align="center">
  <img src="../App/logo.png" alt="Lertaro logo" width="120">
</p>

# ⚡ Lertaro

[English](../README.md) | [简体中文](zh-CN.md) | [繁體中文（香港）](zh-HK.md) | [繁體中文（台灣）](zh-TW.md) | 日本語 | [한국어](ko-KR.md) | [Español](es-ES.md)

> [!CAUTION]
> **セキュリティ警告：公式ソースのみから Lertaro をダウンロードしてください。** リポジトリ `github.com/adelmagical742/Lertaro` およびウェブサイト `adelmagical742.github.io` は Lertaro を偽装した非公式サイトです。これらのサイトからファイルをダウンロードしたり実行したりしないでください。唯一の公式リポジトリは [Lertaro/Lertaro](https://github.com/Lertaro/Lertaro)、公式ウェブサイトは [lertaro.github.io](https://lertaro.github.io/)、公式バイナリは [GitHub Releases](https://github.com/Lertaro/Lertaro/releases) のみで公開されています。

Lertaro は、**.NET 10 (WPF)** をベースに構築された超軽量・高速・高拡張性を誇る Windows 向けグローバル検索およびランチャーツールです。**Listary** や **Everything** のモダンなオープンソース代替として、NTFS の **USN ジャーナル** および $MFT を直接読み取り、低リソース消費で瞬時のファイル検索を実現します。

📖 **[完全ドキュメント・ユーザーマニュアル・開発者マニュアル](https://lertaro.github.io/ja-JP/)**

## 主な特徴

- ⚡ **USN & MFT 低レベルインデックス** —— ディレクトリを巡回走査する代わりに NTFS/ReFS の USN ジャーナルと $MFT を直接読み取り、ミリ秒単位で高速インデックスを構築。FAT32/exFAT 監視やネットワーク共有キャッシュにも対応。
- 🎯 **fzf スタイルあいまい検索とエイリアス** —— 複数キーワードの文字飛び一致、パス絞り込みトークン、非 ASCII ピンインエイリアス変換に対応。
- 📂 **3 つの検索形態とダイアログ統合** —— クイック検索バー、メイン検索ウィンドウに加え、標準の開く/保存ダイアログや主要ファイラー（エクスプローラー、Total Commander、Directory Opus、OneCommander）への自動吸着をサポート。
- 🎬 **アクションメニューと QuickLook プレビュー** —— `Ctrl+O` でアクションメニューと Windows 右クリックメニューを展開、`Alt+P` で QuickLook によるファイル即時プレビューが可能。
- 📊 **即時ディスク容量ツリーマップ分析** —— 既存のインデックスからインタラクティブなツリーマップを即時生成し、再スキャン不要で大容量フォルダーを特定・整理。
- 🧩 **オープンなプラグイン SDK とエコシステム連携** —— .NET 10 による型安全な公式 C# SDK に加え、Flow Launcher コミュニティプラグインとの互換性も確保。
- 🛡️ **3 プロセス分離と完全ローカルプライバシー** —— SYSTEM サービス（`Lertaro.Service`）、WPF アプリ（`Lertaro.App`）、UIPI 権限を越えるフック補助プロセス（`Lertaro.Service --hook`）を厳格に分離。テレメトリを一切送信しません。

検索構文、全ショートカットキー、設定オプションの詳細は[ユーザーマニュアル](https://lertaro.github.io/ja-JP/user-guide/)を、アーキテクチャおよびプラグイン SDK の詳細は[開発者マニュアル](https://lertaro.github.io/ja-JP/dev-guide/)をご覧ください。

## ダウンロード

最新リリースは[公式ホームページ](https://lertaro.github.io/ja-JP/)または以下から直接取得できます：

- **x64 版（Intel / AMD プロセッサ）**
  - [インストーラー (Lertaro-Setup.exe)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup.exe) —— 推奨、バックグラウンドサービスに対応。
  - [ポータブル版 (Lertaro-Portable.zip)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable.zip) —— インストール不要、解凍してすぐに実行可能。
- **ARM64 ネイティブ版（Snapdragon / Windows on ARM デバイス）**
  - [インストーラー (Lertaro-Setup-arm64.exe)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup-arm64.exe) —— ARM デバイス推奨。
  - [ポータブル版 (Lertaro-Portable-arm64.zip)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable-arm64.zip) —— ARM ネイティブポータブル版。

## ソースからのビルド

環境要件：Windows 10/11、.NET 10 SDK、Visual Studio 2022 または JetBrains Rider。インストーラーを作成する場合は [64 ビット版 Inno Setup 7](https://jrsoftware.org/isdl.php#v7) も必要です。

- `build_and_run.bat` —— App/Core/Service/プラグインをリビルドし、ローカルで再起動します。
- `make.bat` —— `dist/` ディレクトリに x64 および ARM64 の Release ビルド（インストーラーおよびポータブル版）を生成します。

詳細なアーキテクチャ設計とプラグイン SDK については[開発者マニュアル](https://lertaro.github.io/ja-JP/dev-guide/)をご参照ください。

## 🎁 寄付とサポート

Lertaro がお役に立ちましたら、開発継続へのご支援をご検討いただけますと幸いです！

- **USDT (TRC20)**：`TNDh3husX1trDW2ZPm4ZZYdoCoCRCZQXn5`

## ライセンス

MIT License のもとで公開されています。
