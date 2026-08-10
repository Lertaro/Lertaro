<p align="center">
  <img src="../App/logo.png" alt="Lertaro logo" width="120">
</p>

# ⚡ Lertaro

[English](../README.md) | [简体中文](zh-CN.md) | [繁體中文（香港）](zh-HK.md) | 繁體中文（台灣） | [日本語](ja-JP.md) | [한국어](ko-KR.md) | [Español](es-ES.md)

> [!CAUTION]
> **安全警告：請只從官方來源下載 Lertaro。** 儲存庫 `github.com/adelmagical742/Lertaro` 和網站 `adelmagical742.github.io` 正在冒充 Lertaro 並散布惡意下載。請勿下載或執行來自這些位址的任何檔案。唯一官方儲存庫是 [Lertaro/Lertaro](https://github.com/Lertaro/Lertaro)，唯一官方網站是 [lertaro.github.io](https://lertaro.github.io/)，官方程式只透過 [GitHub Releases](https://github.com/Lertaro/Lertaro/releases) 發布。即使檔案名稱或內容有所改變，也請一律將上述假冒來源視為不可信。

Lertaro 是一款基於 **.NET 10 (WPF)** 打造的超輕量、極速、高度可擴充的 Windows 全域搜尋與效率啟動工具，是 **Everything** 和 **Listary** 的現代化開源替代方案——透過讀取 NTFS **USN 記錄檔** 與 MFT 直接索引本機磁碟，實現毫秒級、低資源佔用的搜尋體驗。

📖 **[完整文件、使用者手冊與開發者手冊](https://lertaro.github.io/zh-TW/)**

## 核心特色

- **毫秒級索引** —— 直接讀取 NTFS USN 記錄檔/MFT，而不是遞迴掃描目錄；低佔用背景 Service 處理程序即時保持索引同步。
- **FZF 風格模糊比對** —— 支援多關鍵字模糊比對及前綴/後綴/精確/排除搜尋運算子，中文檔名支援拼音別名比對。
- **三種搜尋方式** —— 快速彈出視窗、完整主視窗，以及直接貼靠嵌入檔案總管/原生檔案對話方塊的內嵌搜尋列。
- **QuickLook 預覽**、類右鍵選單的動作選單，快速鍵全部可自訂重新綁定。
- **開放外掛 SDK** —— 可擴充搜尋來源、別名、右鍵選單動作、結果欄、檔案預覽與佈景主題。
- **處理程序隔離** —— SYSTEM 級背景索引服務與使用者態介面處理程序徹底分離。

搜尋語法、每一個快速鍵、每一項設定詳見[使用者手冊](https://lertaro.github.io/zh-TW/user-guide/)；架構設計與外掛 SDK 參考詳見[開發者手冊](https://lertaro.github.io/zh-TW/dev-guide/)。

## 下載

在[專案首頁](https://lertaro.github.io/zh-TW/)取得最新版本，或直接下載：

- **x64 版本（Intel / AMD 處理器）**
  - [安裝程式 Lertaro-Setup.exe](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup.exe) —— 建議使用，支援背景服務。
  - [可攜版 Lertaro-Portable.zip](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable.zip) —— 免安裝，解壓縮即可使用。
- **ARM64 原生版本（高通驍龍 / Windows on ARM 裝置）**
  - [安裝程式 Lertaro-Setup-arm64.exe](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup-arm64.exe) —— ARM 裝置建議使用，原生高效執行。
  - [可攜版 Lertaro-Portable-arm64.zip](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable-arm64.zip) —— ARM 原生免安裝可攜包。

## 從原始碼建置

環境需求：Windows 10/11、.NET 10 SDK、Visual Studio 2022 或 JetBrains Rider；如需產生安裝程式還需要 [Inno Setup](https://jrsoftware.org/isinfo.php)。

- `build_and_run.bat` —— 重新編譯 App/Core/Service/外掛並在本機重新啟動。
- `make.bat` —— 產生 Release 建置，輸出 `dist/` 目錄下的 x64 與 ARM64 安裝程式及可攜包。

完整架構設計與外掛 SDK 詳見[開發者手冊](https://lertaro.github.io/zh-TW/dev-guide/)。

## 🎁 贊助與支持

如果 Lertaro 對你有幫助，非常感謝你考慮贊助支持！

- **USDT (TRC20)**：`TNDh3husX1trDW2ZPm4ZZYdoCoCRCZQXn5`

## 授權條款

本專案採用 MIT License 授權。
