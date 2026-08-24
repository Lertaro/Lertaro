---
layout: home
hero:
  name: Lertaro
  text: 高效能、可擴充的 Windows 本機檢索系統
  tagline: 基於 USN 記錄檔與欄式記憶體索引，毫秒級快速定位檔案與啟動應用，兼具深度對話方塊掛載與開放外掛生態。
  image:
    src: /logo.png
    alt: Lertaro Logo
securityWarning:
  title: "安全警告：僅信任官方來源"
  details: "倉庫 github.com/adelmagical742/Lertaro 和網站 adelmagical742.github.io 正在冒充 Lertaro 並傳播惡意下載。請勿下載或執行來自這些地址的任何檔案，並僅使用下方官方連結。"
features:
  - icon: 💡
    title: Listary 的開源替代方案
    details: 現代化高顏值且高度可擴充的開源檔案檢索與啟動工具，完美替代並拓展傳統商業級桌面檢索工作流程。
  - icon: ⚡
    title: USN 與 MFT 底層索引
    details: 基於 Windows NTFS / ReFS 底層 USN Journal 與 $MFT 機制快速建構索引，支援 FAT32 / exFAT 變動監聽與網路磁碟機快取。
  - icon: 🎯
    title: fzf 模糊比對與拼音別名
    details: 支援字元跳躍模糊命中與路徑定向運算子，內建拼音別名引擎，支援中文檔案名稱首字母與全拼極速檢索。
  - icon: 🖱️
    title: 原生檔案對話方塊深度掛載
    details: 自動掛載於 Windows 原生「開啟 / 另存為」對話方塊及 Explorer、Total Commander，雙向同步選取狀態與目前工作路徑。
  - icon: 🎬
    title: 動作選單與 QuickLook 預覽
    details: 選取項目按 Ctrl+O 呼出完整動作選單與原生 Shell 右鍵，按下 Alt+P 即可透過 QuickLook 即時預覽文件與影音。
  - icon: 📊
    title: 即時磁碟空間透視分析
    details: 基於已有記憶體索引直接產生矩形樹（Treemap）空間佔用圖，免去漫長重新掃描磁碟的過程，支援快速定位大檔案。
  - icon: 🧩
    title: 開放外掛 SDK 與生態相容
    details: 提供基於 .NET 10 的官方 C# 外掛開發介面，並支援橋接執行 Flow Launcher 社群外掛與自訂工作流程。
  - icon: 🛡️
    title: 三程序架構與離線隱私
    details: SYSTEM 索引服務、使用者態 App 與獨立 Hook 程序安全隔離；純本機運行，不收集任何使用者隱私資料。
---
