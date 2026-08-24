<p align="center">
  <img src="../App/logo.png" alt="Lertaro logo" width="120">
</p>

# ⚡ Lertaro

[English](../README.md) | [简体中文](zh-CN.md) | [繁體中文（香港）](zh-HK.md) | 繁體中文（台灣） | [日本語](ja-JP.md) | [한국어](ko-KR.md) | [Español](es-ES.md)

> [!CAUTION]
> **安全警告：請僅從官方來源下載 Lertaro。** 倉庫 `github.com/adelmagical742/Lertaro` 和網站 `adelmagical742.github.io` 正在冒充 Lertaro 並傳播惡意下載。請勿下載或執行來自這些地址的任何檔案。唯一官方倉庫是 [Lertaro/Lertaro](https://github.com/Lertaro/Lertaro)，唯一官方網站是 [lertaro.github.io](https://lertaro.github.io/)，官方程式僅透過 [GitHub Releases](https://github.com/Lertaro/Lertaro/releases) 發布。即使檔案名稱或內容發生變化，也請始終將上述假冒來源視為不可信。

Lertaro 是一款基於 **.NET 10 (WPF)** 打造的超輕量、極速、高度可擴充的 Windows 全域搜尋與效率啟動工具，是 **Listary** 和 **Everything** 的現代化開源替代——透過讀取 NTFS **USN 記錄檔** 與 $MFT 直接索引本機磁碟，實現毫秒級、低資源佔用的檢索體驗。

📖 **[完整文檔、使用者手冊與開發手冊](https://lertaro.github.io/zh-TW/)**

## 核心特性

- ⚡ **USN 與 MFT 底層索引** —— 直接讀取 NTFS / ReFS 磁碟底層 USN Journal 與 $MFT，秒級建立全盤索引，支援 FAT32 / exFAT 變動監聽與網路磁碟機快取。
- 🎯 **fzf 模糊比對與拼音別名** —— 支援字元跳躍模糊命中、路徑定向運算子與中文檔案名稱首字母/全拼極速檢索。
- 📂 **三大搜尋形態與深度掛載** —— 置中快速浮動視窗、完整主視窗，並自動掛載於 Windows 原生 Open/Save 對話方塊與主流檔案管理員（Explorer、Total Commander、Directory Opus、OneCommander）。
- 🎬 **動作選單與 QuickLook 預覽** —— `Ctrl+O` 呼出動作選單與原生 Shell 右鍵，`Alt+P` 觸發 QuickLook 即時預覽文件與影音。
- 📊 **即時磁碟空間透視分析** —— 基於已有記憶體索引直接產生矩形樹（Treemap）空間佔用圖，免去漫長的磁碟重掃過程。
- 🧩 **開放外掛 SDK 與生態相容** —— 基於 .NET 10 的官方強型別 C# SDK，並相容執行 Flow Launcher 社群外掛與自訂工作流程。
- 🛡️ **三程序架構與離線隱私** —— SYSTEM 索引服務（`Lertaro.Service`）、使用者態 App（`Lertaro.App`）與獨立 Hook 程序（`Lertaro.Service --hook`）安全隔離；純本機離線運行，零雲端遙測。

搜尋語法、每一個快速鍵、每一項設定詳見[使用者手冊](https://lertaro.github.io/zh-TW/user-guide/)；架構設計與外掛 SDK 參考詳見[開發手冊](https://lertaro.github.io/zh-TW/dev-guide/)。

## 下載

在[專案首頁](https://lertaro.github.io/zh-TW/)獲取最新版本，或直接下載：

- **x64 版本（Intel / AMD 處理器）**
  - [安裝套件 Lertaro-Setup.exe](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup.exe) —— 推薦，支援後台系統服務。
  - [便攜版 Lertaro-Portable.zip](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable.zip) —— 綠色免安裝，解壓即用。
- **ARM64 原生版本（驍龍 / Windows on ARM 裝置）**
  - [安裝套件 Lertaro-Setup-arm64.exe](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup-arm64.exe) —— ARM 裝置推薦，原生高效運行。
  - [便攜版 Lertaro-Portable-arm64.zip](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable-arm64.zip) —— ARM 原生免安裝便攜包。

## 從原始碼建構

環境要求：Windows 10/11、.NET 10 SDK、Visual Studio 2022 或 JetBrains Rider；如需產生安裝套件還需要 [Inno Setup](https://jrsoftware.org/isinfo.php)。

- `build_and_run.bat` —— 重新編譯 App/Core/Service/外掛並在本機重新啟動，適合日常開發除錯。
- `make.bat` —— 產生 Release 建構，產出 `dist/` 目錄下的 x64 與 ARM64 安裝套件及便攜包。

完整架構設計與外掛 SDK 詳見[開發手冊](https://lertaro.github.io/zh-TW/dev-guide/)。

## 🎁 捐贈與支援

如果 Lertaro 對你有幫助，非常感謝你考慮捐贈支援！

- **USDT (TRC20)**：`TNDh3husX1trDW2ZPm4ZZYdoCoCRCZQXn5`

## 授權條款

本專案基於 MIT License 開源。
