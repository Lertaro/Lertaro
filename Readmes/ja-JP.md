<p align="center">
  <img src="../App/logo.png" alt="Lertaro logo" width="120">
</p>

# ⚡ Lertaro

[English](../README.md) | [简体中文](zh-CN.md) | [繁體中文（香港）](zh-HK.md) | [繁體中文（台灣）](zh-TW.md) | 日本語 | [한국어](ko-KR.md) | [Español](es-ES.md)

> [!CAUTION]
> **セキュリティ警告：Lertaro は必ず公式配布元からダウンロードしてください。** リポジトリ `github.com/adelmagical742/Lertaro` とウェブサイト `adelmagical742.github.io` は Lertaro を偽装し、悪意のあるダウンロードを配布しています。これらの場所からファイルをダウンロードしたり実行したりしないでください。唯一の公式リポジトリは [Lertaro/Lertaro](https://github.com/Lertaro/Lertaro)、唯一の公式ウェブサイトは [lertaro.github.io](https://lertaro.github.io/) であり、公式バイナリは [GitHub Releases](https://github.com/Lertaro/Lertaro/releases) からのみ配布されます。ファイル名や内容が変わっても、上記の偽装配布元は信頼しないでください。

Lertaro は **.NET 10 (WPF)** で構築された、超軽量・高性能・拡張可能な Windows 向けグローバル検索/効率化ランチャーです。**Everything** や **Listary** に代わるモダンなオープンソースの選択肢で、NTFS の **USN Journal** と MFT を直接読み取ってローカルドライブをインデックス化し、瞬時かつ低リソースな検索を実現します。

📖 **[ドキュメント全体・ユーザーマニュアル・開発者マニュアル](https://lertaro.github.io/ja-JP/)**

## 主な特長

- **瞬時のインデックス作成** —— ディレクトリを走査する代わりに NTFS の USN Journal/MFT を直接読み取ります。軽量なバックグラウンドサービスがリアルタイムでインデックスを同期し続けます。
- **FZF スタイルのあいまい検索** —— 前方一致/後方一致/完全一致/除外演算子を備えた複数キーワードのあいまい検索に加え、中国語ファイル名向けのピンインエイリアスにも対応。
- **3 通りの検索方法** —— クイックポップアップウィンドウ、フルサイズのメインウィンドウ、およびエクスプローラーやネイティブのファイルダイアログに直接ドッキングするインライン検索バー。
- **QuickLook プレビュー**、右クリックメニュー風のアクションメニュー、すべて再設定可能なホットキー。
- **オープンなプラグイン SDK** —— 検索プロバイダー、エイリアス、コンテキストメニューアクション、結果列、プレビュー、テーマを拡張できます。
- **プロセスの分離** —— SYSTEM レベルのインデックスサービスは、ユーザー単位のアプリ UI とは別プロセスとして動作します。

検索構文、すべてのホットキー、すべての設定項目については[ユーザーマニュアル](https://lertaro.github.io/ja-JP/user-guide/)を、アーキテクチャとプラグイン SDK リファレンスについては[開発者マニュアル](https://lertaro.github.io/ja-JP/dev-guide/)をご覧ください。

## ダウンロード

[公式ホームページ](https://lertaro.github.io/ja-JP/)または直接リンクから最新リリースを入手してください:

- **x64 版（Intel / AMD プロセッサ）**
  - [インストーラー (Lertaro-Setup.exe)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup.exe) — 推奨。バックグラウンドサービスに対応。
  - [ポータブル版 (Lertaro-Portable.zip)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable.zip) — インストール不要、解凍してすぐ使えます。
- **ARM64 ネイティブ版（Snapdragon / Windows on ARM デバイス）**
  - [インストーラー (Lertaro-Setup-arm64.exe)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup-arm64.exe) — ARM デバイスに推奨、ネイティブで高速動作。
  - [ポータブル版 (Lertaro-Portable-arm64.zip)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable-arm64.zip) — ARM ネイティブポータブル版。

## ソースからビルドする

必要環境: Windows 10/11、.NET 10 SDK、Visual Studio 2022 または JetBrains Rider。インストーラーをビルドする場合は [Inno Setup](https://jrsoftware.org/isinfo.php) も必要です。

- `build_and_run.bat` —— App/Core/Service/プラグインを再ビルドし、ローカルで再起動します。
- `make.bat` —— Release ビルドを生成し、`dist/` 内に x64 および ARM64 のインストーラーとポータブル版を出力します。

アーキテクチャとプラグイン SDK の詳細については[開発者マニュアル](https://lertaro.github.io/ja-JP/dev-guide/)をご覧ください。

## 🎁 サポート・寄付

Lertaro がお役に立てたなら、ぜひ寄付をご検討ください！

- **USDT (TRC20)**: `TNDh3husX1trDW2ZPm4ZZYdoCoCRCZQXn5`

## ライセンス

MIT License。
