<p align="center">
  <img src="../App/logo.png" alt="Lertaro logo" width="120">
</p>

# ⚡ Lertaro

[English](../README.md) | [简体中文](zh-CN.md) | 繁體中文（香港） | [繁體中文（台灣）](zh-TW.md) | [日本語](ja-JP.md) | [한국어](ko-KR.md) | [Español](es-ES.md)

Lertaro 是一款基於 **.NET 10 (WPF)** 打造的超輕量、極速、高度可擴展的 Windows 全局檢索與效率啟動工具，是 **Everything** 和 **Listary** 的現代化開源替代——通過讀取 NTFS **USN 日誌** 與 MFT 直接索引本地磁碟，實現毫秒級、低資源佔用的檢索體驗。

📖 **[完整文檔、使用者手冊與開發者手冊](https://lertaro.github.io/zh-HK/)**

## 核心特性

- **毫秒級索引** —— 直接讀取 NTFS USN 日誌/MFT，而不是遞歸掃描目錄；低佔用後臺 Service 進程實時保持索引同步。
- **FZF 風格模糊搜尋** —— 支援多關鍵詞模糊匹配及前綴/後綴/精確/排除搜尋操作符，中文檔案名支援拼音別名匹配。
- **三種搜尋方式** —— 快速彈窗、完整主視窗，以及直接貼靠嵌入檔案總管/原生檔案對話方塊的內聯搜尋欄。
- **QuickLook 預覽**、類右鍵選單的動作選單，熱鍵全部可自訂重新綁定。
- **開放插件 SDK** —— 可擴展搜尋來源、別名、右鍵選單動作、結果欄、檔案預覽與主題。
- **進程隔離** —— SYSTEM 級後臺索引服務與使用者態介面進程徹底分離。

搜尋語法、每一個熱鍵、每一項設定詳見[使用者手冊](https://lertaro.github.io/zh-HK/user-guide/)；架構設計與插件 SDK 參考詳見[開發者手冊](https://lertaro.github.io/zh-HK/dev-guide/)。

## 下載

在[專案主頁](https://lertaro.github.io/zh-HK/)取得最新版本，或直接下載：

- **x64 版本（Intel / AMD 處理器）**
  - [安裝包 Lertaro-Setup.exe](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup.exe) —— 推薦，支援後臺服務。
  - [便攜版 Lertaro-Portable.zip](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable.zip) —— 免安裝，解壓即用。
- **ARM64 原生版本（高通驍龍 / Windows on ARM 裝置）**
  - [安裝包 Lertaro-Setup-arm64.exe](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup-arm64.exe) —— ARM 裝置推薦，原生高效運行。
  - [便攜版 Lertaro-Portable-arm64.zip](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable-arm64.zip) —— ARM 原生免安裝便攜包。

## 從原始碼建置

環境要求：Windows 10/11、.NET 10 SDK、Visual Studio 2022 或 JetBrains Rider；如需生成安裝包還需要 [Inno Setup](https://jrsoftware.org/isinfo.php)。

- `build_and_run.bat` —— 重新編譯 App/Core/Service/插件並在本機重新啟動。
- `make.bat` —— 產生 Release 建置，輸出 `dist/` 目錄下的 x64 與 ARM64 安裝包及便攜包。

完整架構設計與插件 SDK 詳見[開發者手冊](https://lertaro.github.io/zh-HK/dev-guide/)。

## 🎁 捐款與支持

如果 Lertaro 對你有幫助，非常感謝你考慮捐款支持！

- **USDT (TRC20)**：`TNDh3husX1trDW2ZPm4ZZYdoCoCRCZQXn5`

## 授權條款

本專案基於 MIT License 開源。
