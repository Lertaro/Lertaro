<script setup>
import { computed } from 'vue'
import { useData } from 'vitepress'

const { lang } = useData()
const localePrefix = () => (lang.value === 'en-US' ? '' : `/${lang.value}`)

const content = computed(() => {
  if (lang.value === 'zh-CN') {
    return {
      archTitle: '三进程隔离架构与运行模型',
      archSubtitle: '严格划分特权边界，确保系统级检索性能与前台 UI 的极致稳定性',
      archLink: '阅读完整架构设计文档 →',
      proc1Role: 'SYSTEM 服务',
      proc1Name: 'Lertaro.Service',
      proc1Desc: '以 LocalSystem 权限常驻后台，直接读取 NTFS / ReFS 底层 USN Journal 与 $MFT，监听 FAT32 / exFAT 变动，并通过命名管道提供高性能只读数据检索。',
      proc2Role: '用户态前台',
      proc2Name: 'Lertaro.App',
      proc2Desc: '标准用户 Session 桌面程序，承载快速搜索居中浮窗、完整主窗口、设置中心、动作菜单以及插件管理，不持有任何多余系统特权。',
      proc3Role: '窗口与钩子',
      proc3Name: 'Lertaro.Service --hook',
      proc3Desc: '独立特权辅助进程，托管低级全局键盘与鼠标监听，突破 Windows UIPI 权限隔离以无缝嵌入管理员权限对话框，同时隔离异常崩溃风险。',

      compatTitle: '系统兼容与生态支持',
      compatSubtitle: '开箱即用的深度桌面集成与外部生产力工具协同',
      osTitle: '🖥️ 操作系统支持',
      osDesc: '完美适配 Windows 10 与 Windows 11，原生提供 x64（主流 PC）与 ARM64（如高通骁龙 Windows 笔记本）编译版本。',
      fmTitle: '📂 文件管理器挂载',
      fmDesc: '支持自动挂载并实时感知当前工作路径：Windows 文件资源管理器、Total Commander、Directory Opus 与 OneCommander。',
      toolsTitle: '🔌 生产力工具联动',
      toolsDesc: '原生无缝调用 QuickLook (Alt+P) 文件即时预览，支持 LocalSend 局域网传输，并提供 Flow Launcher 社区插件桥接支持。',

      faqTitle: '常见问题与解答',
      faqSubtitle: '针对架构设计、系统服务与使用模式的核心技术解答',
      faqs: [
        {
          q: '为什么需要安装后台 Windows 服务？',
          a: '后台服务以 LocalSystem 权限直接读取 NTFS/ReFS 磁盘底层的 USN Journal，无需用户在前台频繁弹窗赋予管理员权限，既保证了全盘变更监听的高性能，又避免了让前台 UI 进程持有不必要的系统高特权。'
        },
        {
          q: 'Lertaro 与 Everything、Listary 的主要区别与定位是什么？',
          a: 'Lertaro 专注于像 Listary 一样深度挂载文件对话框与即时模糊查找，同时具备 Everything 级别的底层 USN 秒级索引性能，并提供现代 .NET 10 开放插件生态与即时磁盘空间分析。'
        },
        {
          q: '便携版（Portable）与安装版（Installer）有什么区别？',
          a: '安装版会自动注册并管理后台索引服务，适合长期作为系统主力检索工具；便携版完全免安装解压即用，若需要后台全盘索引服务也可在设置中心一键按需注册与卸载。'
        },
        {
          q: '检索数据会上传到云端或产生网络遥测吗？',
          a: '完全不会。Lertaro 为 100% 本地纯离线运行工具，不包含任何后台遥测、数据统计上报或强制云端连接，严格保障本地隐私安全。'
        }
      ],

      commTitle: '开源社区与参与贡献',
      commSubtitle: 'Lertaro 采用开源协议发布，欢迎参与代码贡献、插件开发与问题反馈',
      commGithubTitle: 'GitHub 源码仓库',
      commGithubDesc: '查看项目源代码、Release 发行版与提交历史',
      commIssuesTitle: '提交反馈与建议',
      commIssuesDesc: '遇到 Bug 或有新功能想法？欢迎提交 Issue 与讨论',
      commDevTitle: '插件开发指南',
      commDevDesc: '基于 .NET 10 SDK 编写你的第一个专属扩展插件',
      commDonateTitle: '赞助与支持',
      commDonateDesc: '如果 Lertaro 提升了你的工作效率，欢迎赞助支持'
    }
  } else if (lang.value === 'zh-HK' || lang.value === 'zh-TW') {
    return {
      archTitle: '三程序隔離架構與運行模型',
      archSubtitle: '嚴格劃分特權邊界，確保系統級檢索效能與前景 UI 的極致穩定性',
      archLink: '閱讀完整架構設計文檔 →',
      proc1Role: 'SYSTEM 服務',
      proc1Name: 'Lertaro.Service',
      proc1Desc: '以 LocalSystem 權限常駐背景，直接讀取 NTFS / ReFS 底層 USN Journal 與 $MFT，監聽 FAT32 / exFAT 變動，並透過具名管道提供高效能唯讀資料檢索。',
      proc2Role: '使用者態前景',
      proc2Name: 'Lertaro.App',
      proc2Desc: '標準使用者 Session 桌面程式，承載快速搜尋置中浮動視窗、完整主視窗、設定中心、動作選單以及外掛管理，不持有任何多餘系統特權。',
      proc3Role: '視窗與掛鉤',
      proc3Name: 'Lertaro.Service --hook',
      proc3Desc: '獨立特權輔助程序，託管低階全域鍵盤與滑鼠監聽，突破 Windows UIPI 權限隔離以無縫嵌入管理員權限對話方塊，同時隔離異常當機風險。',

      compatTitle: '系統相容與生態支援',
      compatSubtitle: '開箱即用的深度桌面整合與外部生產力工具協同',
      osTitle: '🖥️ 作業系統支援',
      osDesc: '完美適配 Windows 10 與 Windows 11，原生提供 x64（主流 PC）與 ARM64（如高通驍龍 Windows 筆記型電腦）編譯版本。',
      fmTitle: '📂 檔案管理員掛載',
      fmDesc: '支援自動掛載並即時感知目前工作路徑：Windows 檔案總管、Total Commander、Directory Opus 與 OneCommander。',
      toolsTitle: '🔌 生產力工具連動',
      toolsDesc: '原生無縫調用 QuickLook (Alt+P) 檔案即時預覽，支援 LocalSend 區域網路傳輸，並提供 Flow Launcher 社群外掛橋接支援。',

      faqTitle: '常見問題與解答',
      faqSubtitle: '針對架構設計、系統服務與使用模式的核心技術解答',
      faqs: [
        {
          q: '為什麼需要安裝背景 Windows 服務？',
          a: '背景服務以 LocalSystem 權限直接讀取 NTFS/ReFS 磁碟底層的 USN Journal，無需使用者在前景頻繁彈窗賦予管理員權限，既保證了全盤變更監聽的高效能，又避免了讓前景 UI 程序持有不必要的系統高特權。'
        },
        {
          q: 'Lertaro 與 Everything、Listary 的主要區別與定位是什麼？',
          a: 'Lertaro 專注於像 Listary 一樣深度掛載檔案對話方塊與即時模糊查找，同時具備 Everything 等級的底層 USN 秒級索引效能，並提供現代 .NET 10 開放外掛生態與即時磁碟空間分析。'
        },
        {
          q: '便攜版（Portable）與安裝版（Installer）有什麼區別？',
          a: '安裝版會自動註冊並管理背景索引服務，適合長期作為系統主力檢索工具；便攜版完全免安裝解壓即用，若需要背景全盤索引服務也可在設定中心一鍵按需註冊與解除安裝。'
        },
        {
          q: '檢索資料會上傳到雲端或產生網路遙測嗎？',
          a: '完全不會。Lertaro 為 100% 本機純離線運行工具，不包含任何背景遙測、資料統計回報或強制雲端連線，嚴格保障本機隱私安全。'
        }
      ],

      commTitle: '開源社群與參與貢獻',
      commSubtitle: 'Lertaro 採用開源協議發布，歡迎參與程式碼貢獻、外掛開發與問題反饋',
      commGithubTitle: 'GitHub 原始碼倉庫',
      commGithubDesc: '查看專案原始碼、Release 發行版與提交歷程',
      commIssuesTitle: '提交反饋與建議',
      commIssuesDesc: '遇到 Bug 或有新功能想法？歡迎提交 Issue 與討論',
      commDevTitle: '外掛開發指南',
      commDevDesc: '基於 .NET 10 SDK 撰寫你的第一個專屬擴充外掛',
      commDonateTitle: '贊助與支援',
      commDonateDesc: '如果 Lertaro 提升了你的工作效率，歡迎贊助支援'
    }
  } else if (lang.value === 'ja-JP') {
    return {
      archTitle: '3 プロセス分離アーキテクチャ',
      archSubtitle: '特権境界を厳格に分離し、システム全体の安定性と安全性を最大化',
      archLink: 'アーキテクチャの詳細ドキュメントを見る →',
      proc1Role: 'SYSTEM サービス',
      proc1Name: 'Lertaro.Service',
      proc1Desc: 'LocalSystem 権限でバックグラウンド常駐。NTFS / ReFS の USN ジャーナルと $MFT を直接読み取り、名前付きパイプを通じて超高速な読み取り専用データを提供。',
      proc2Role: 'ユーザーアプリ',
      proc2Name: 'Lertaro.App',
      proc2Desc: '標準ユーザー権限で動作する WPF デスクトップアプリ。クイック検索バー、メイン検索ウィンドウ、設定画面、プラグイン実行を安全に担当。',
      proc3Role: 'フック＆統合',
      proc3Name: 'Lertaro.Service --hook',
      proc3Desc: '独立した特権補助プロセス。低レベルキーボードフックを管理し、UIPI 権限の壁を越えて管理者権限ダイアログともクラッシュ耐性を保ちながら連携。',

      compatTitle: 'システム互換性とツール連携',
      compatSubtitle: 'Windows デスクトップとの緊密な統合と外部ツールの活用',
      osTitle: '🖥️ OS サポート',
      osDesc: 'Windows 10 および Windows 11 に対応。x64（一般的な PC）および ARM64（Snapdragon 搭載 PC）のネイティブバイナリを提供。',
      fmTitle: '📂 ファイラー統合',
      fmDesc: 'エクスプローラー、Total Commander、Directory Opus、OneCommander に自動吸着し、作業フォルダーをリアルタイム検出。',
      toolsTitle: '🔌 外部ツール連携',
      toolsDesc: 'Alt+P キーでの QuickLook プレビュー、LocalSend による LAN ファイル転送、Flow Launcher プラグインとの相互運用に対応。',

      faqTitle: 'よくある質問 (FAQ)',
      faqSubtitle: 'アーキテクチャ、バックグラウンドサービス、機能に関する技術的解説',
      faqs: [
        {
          q: 'なぜバックグラウンド Windows サービスが必要なのですか？',
          a: 'NTFS/ReFS の USN ジャーナルを LocalSystem 権限で直接読み取るためです。UAC 昇格ダイアログなしで安全かつ高速に変更を監視でき、UI アプリに不要な権限を持たせません。'
        },
        {
          q: 'Everything や Listary との主な違いは何ですか？',
          a: 'Lertaro は Listary のようなファイルダイアログへの直接ドッキングとあいまい検索を備えつつ、Everything 級の高速 USN インデックスとモダンな .NET 10 プラグイン機構、容量分析を兼ね備えています。'
        },
        {
          q: 'ポータブル版とインストーラー版の違いは何ですか？',
          a: 'インストーラー版はバックグラウンドサービスを自動登録します。ポータブル版は解凍後すぐに利用でき、必要に応じて設定画面からサービスを登録・解除できます。'
        },
        {
          q: '検索データがクラウドに送信されることはありますか？',
          a: '一切ありません。Lertaro は 100% 完全ローカルで動作し、テレメトリ送信や外部通信は一切行いません。'
        }
      ],

      commTitle: 'オープンソースコミュニティと貢献',
      commSubtitle: 'Lertaro はオープンソースで開発されています。改善やプラグインの提案を歓迎します',
      commGithubTitle: 'GitHub リポジトリ',
      commGithubDesc: 'ソースコード、リリース一覧、コミット履歴の確認',
      commIssuesTitle: 'フィードバックと提案',
      commIssuesDesc: 'バグ報告や新機能の提案は Issue よりお寄せください',
      commDevTitle: 'プラグイン開発ガイド',
      commDevDesc: '.NET 10 SDK を使って独自の拡張機能を作成',
      commDonateTitle: 'スポンサーと支援',
      commDonateDesc: 'Lertaro が役立っている場合、開発支援をご検討ください'
    }
  } else if (lang.value === 'ko-KR') {
    return {
      archTitle: '3 프로세스 격리 아키텍처',
      archSubtitle: '권한 경계를 엄격히 구분하여 최고 수준의 검색 성능과 데스크톱 안정성 확보',
      archLink: '아키텍처 상세 문서 보기 →',
      proc1Role: 'SYSTEM 서비스',
      proc1Name: 'Lertaro.Service',
      proc1Desc: 'LocalSystem 권한으로 상주하며 NTFS / ReFS USN Journal 및 $MFT를 직접 파싱하고 네임드 파이프를 통해 초고속 인덱스 검색 서비스를 제공합니다.',
      proc2Role: '사용자 UI 앱',
      proc2Name: 'Lertaro.App',
      proc2Desc: '표준 사용자 세션의 WPF 데스크톱 프로그램으로 퀵 검색창, 메인 윈도우, 설정 센터, 액션 메뉴 및 플러그인을 안전하게 구동합니다.',
      proc3Role: '창 도킹 및 Hook',
      proc3Name: 'Lertaro.Service --hook',
      proc3Desc: '독립 특권 보조 프로세스로 전역 키보드/마우스 훅을 관리하며, UIPI 권한 격리를 우회하여 관리자 권한 대화상자에도 안전하게 도킹합니다.',

      compatTitle: '시스템 호환성 및 생태계 연동',
      compatSubtitle: '강력한 Windows 데스크톱 통합 및 서드파티 생산성 도구 연계',
      osTitle: '🖥️ 운영체제 지원',
      osDesc: 'Windows 10 및 Windows 11 완벽 지원. x64(일반 PC) 및 ARM64(Windows on ARM) 네이티브 바이너리 동시 제공.',
      fmTitle: '📂 파일 관리자 연동',
      fmDesc: 'Windows 파일 탐색기, Total Commander, Directory Opus, OneCommander의 작업 디렉토리를 실시간 감지하여 자동 도킹.',
      toolsTitle: '🔌 생산성 도구 연동',
      toolsDesc: 'Alt+P를 통한 QuickLook 파일 즉시 미리보기, LocalSend 로컬 네트워크 전송, Flow Launcher 커뮤니티 플러그인 호환.',

      faqTitle: '자주 묻는 질문 (FAQ)',
      faqSubtitle: '아키텍처 설계, 백그라운드 서비스 및 사용 방식에 대한 핵심 기술 안내',
      faqs: [
        {
          q: '왜 백그라운드 Windows 서비스가 필요한가요?',
          a: 'NTFS/ReFS의 USN 저널을 LocalSystem 권한으로 직접 읽기 위함입니다. UAC 팝업 없이 초고속으로 파일 변경을 감지하며, UI 프로세스에 불필요한 시스템 권한을 부여하지 않아 안전합니다.'
        },
        {
          q: 'Everything, Listary와의 주요 차이점은 무엇인가요?',
          a: 'Lertaro는 Listary와 같은 네이티브 파일 대화상자 도킹 및 퍼지 검색을 지원하면서도, Everything 수준의 USN 저널 인덱싱 속도, 최신 .NET 10 플러그인 SDK, 실시간 디스크 공간 분석을 함께 제공합니다.'
        },
        {
          q: '포터블 버전과 인스톨러 버전의 차이는 무엇인가요?',
          a: '인스톨러 버전은 백그라운드 서비스를 자동 등록합니다. 포터블 버전은 무설치로 즉시 실행할 수 있으며, 필요 시 설정 센터에서 서비스를 원클릭으로 등록할 수 있습니다.'
        },
        {
          q: '검색 데이터가 외부 서버로 전송되나요?',
          a: '전혀 전송되지 않습니다. Lertaro는 100% 로컬 오프라인 전용 도구이며 원격 측정 및 외부 통신이 일체 없습니다.'
        }
      ],

      commTitle: '오픈소스 커뮤니티 및 기여',
      commSubtitle: 'Lertaro는 오픈소스로 개발됩니다. 코드 기여, 플러그인 제작 및 피드백을 환영합니다',
      commGithubTitle: 'GitHub 저장소',
      commGithubDesc: '소스 코드, 릴리스 내역 및 커밋 기록 확인',
      commIssuesTitle: '피드백 및 이슈 등록',
      commIssuesDesc: '버그 신고 및 새로운 기능 제안을 남겨주세요',
      commDevTitle: '플러그인 개발 가이드',
      commDevDesc: '.NET 10 SDK를 활용한 맞춤형 확장 기능 제작',
      commDonateTitle: '후원 및 지원',
      commDonateDesc: 'Lertaro가 유용하셨다면 개발 후원을 검토해 보세요'
    }
  } else if (lang.value === 'es-ES') {
    return {
      archTitle: 'Arquitectura de Aislamiento de 3 Procesos',
      archSubtitle: 'Separación estricta de privilegios para máxima estabilidad y rendimiento en Windows',
      archLink: 'Ver documento de arquitectura completo →',
      proc1Role: 'Servicio SYSTEM',
      proc1Name: 'Lertaro.Service',
      proc1Desc: 'Servicio en segundo plano con privilegios LocalSystem. Lee directamente el USN Journal y $MFT en NTFS/ReFS y sirve búsquedas mediante canalizaciones con nombre (Named Pipes).',
      proc2Role: 'App de Usuario',
      proc2Name: 'Lertaro.App',
      proc2Desc: 'Aplicación WPF en la sesión de usuario estándar. Aloja la barra de búsqueda rápida, ventana principal, configuración, menú de acciones y plugins sin privilegios elevados.',
      proc3Role: 'Hook y Diálogos',
      proc3Name: 'Lertaro.Service --hook',
      proc3Desc: 'Proceso auxiliar aislado que gestiona el gancho de teclado global y supera el aislamiento UIPI para acoplarse a ventanas con privilegios de administrador con total estabilidad.',

      compatTitle: 'Compatibilidad y Herramientas Soportadas',
      compatSubtitle: 'Integración profunda con el sistema y herramientas de productividad externas',
      osTitle: '🖥️ Sistemas Operativos',
      osDesc: 'Compatible con Windows 10 y Windows 11. Compilaciones nativas para x64 (PC estándar) y ARM64 (Windows on ARM).',
      fmTitle: '📂 Gestores de Archivos',
      fmDesc: 'Detección de ruta activa y acople automático en el Explorador de Windows, Total Commander, Directory Opus y OneCommander.',
      toolsTitle: '🔌 Ecosistema y Productividad',
      toolsDesc: 'Vista previa instantánea con QuickLook (Alt+P), transferencias con LocalSend y compatibilidad con plugins de Flow Launcher.',

      faqTitle: 'Preguntas Frecuentes (FAQ)',
      faqSubtitle: 'Respuestas técnicas sobre arquitectura, servicios del sistema y funcionamiento',
      faqs: [
        {
          q: '¿Por qué se requiere un servicio de Windows en segundo plano?',
          a: 'El servicio lee el USN Journal a nivel de LocalSystem sin solicitar ventanas UAC en primer plano. Garantiza un rendimiento óptimo sin otorgar privilegios innecesarios a la interfaz de usuario.'
        },
        {
          q: '¿En qué se diferencia Lertaro de Everything y Listary?',
          a: 'Lertaro combina la integración en diálogos nativos y búsqueda difusa estilo Listary con la velocidad de indexación USN de Everything, añadiendo un SDK en .NET 10 y análisis de espacio en disco.'
        },
        {
          q: '¿Cuál es la diferencia entre la versión instalable y la portable?',
          a: 'El instalador registra automáticamente el servicio. La versión portable se ejecuta directamente tras descomprimir y permite registrar el servicio desde la configuración si se desea.'
        },
        {
          q: '¿Se recopilan o transmiten datos de búsqueda a la nube?',
          a: 'En absoluto. Lertaro es 100% local y offline, sin telemetría ni conexiones externas de ningún tipo.'
        }
      ],

      commTitle: 'Comunidad y Contribución',
      commSubtitle: 'Lertaro es un proyecto de código abierto. Agradecemos contribuciones, sugerencias y desarrollo de plugins',
      commGithubTitle: 'Repositorio en GitHub',
      commGithubDesc: 'Explora el código fuente, versiones publicadas e historial',
      commIssuesTitle: 'Reportar Errores y Sugerencias',
      commIssuesDesc: '¿Tienes una idea o encontraste un bug? Abre un Issue',
      commDevTitle: 'Guía de Plugins',
      commDevDesc: 'Crea tu primera extensión con el SDK de .NET 10',
      commDonateTitle: 'Donaciones y Soporte',
      commDonateDesc: 'Si Lertaro mejora tu productividad, apoya el proyecto'
    }
  } else {
    // English default
    return {
      archTitle: '3-Process Isolation Architecture',
      archSubtitle: 'Strict privilege boundaries ensure sub-millisecond search performance and uncompromised UI stability',
      archLink: 'Read full architecture documentation →',
      proc1Role: 'SYSTEM Service',
      proc1Name: 'Lertaro.Service',
      proc1Desc: 'Runs as LocalSystem in the background to directly read NTFS/ReFS USN Change Journals and $MFT tables, serving high-throughput read-only search queries via named pipes.',
      proc2Role: 'User-Mode UI',
      proc2Name: 'Lertaro.App',
      proc2Desc: 'Standard user-session WPF application hosting the Quick Search bar, Full Search window, settings center, action menus, and plugin loader with zero elevated rights.',
      proc3Role: 'Hook & Docking',
      proc3Name: 'Lertaro.Service --hook',
      proc3Desc: 'Isolated privileged helper managing global keyboard hooks, bypassing UIPI security boundaries to attach seamlessly to elevated admin dialogs without crash propagation.',

      compatTitle: 'Compatibility & Supported Integrations',
      compatSubtitle: 'Deep desktop integration and native collaboration with external productivity tools',
      osTitle: '🖥️ Operating Systems',
      osDesc: 'Fully optimized for Windows 10 and Windows 11, with native builds for both x64 (standard PCs) and ARM64 (Windows on ARM).',
      fmTitle: '📂 File Managers',
      fmDesc: 'Automatic active path detection and docking support for Windows File Explorer, Total Commander, Directory Opus, and OneCommander.',
      toolsTitle: '🔌 Productivity Ecosystem',
      toolsDesc: 'Seamless Alt+P preview via QuickLook, local network sharing with LocalSend, and cross-ecosystem Flow Launcher plugin compatibility.',

      faqTitle: 'Frequently Asked Questions (FAQ)',
      faqSubtitle: 'Technical explanations on architecture, system service, and workflow patterns',
      faqs: [
        {
          q: 'Why does Lertaro use a background Windows Service?',
          a: 'The background service reads low-level NTFS/ReFS USN Change Journals under LocalSystem without prompting frequent UAC dialogs in the UI, keeping full-disk change monitoring fast and isolating high privileges from the user interface.'
        },
        {
          q: 'How does Lertaro differ from Everything and Listary?',
          a: 'Lertaro unites deep file dialog docking and jump fuzzy search (like Listary) with raw USN sub-millisecond indexing (like Everything), while offering a modern .NET 10 Plugin SDK and real-time Treemap disk space visualization.'
        },
        {
          q: 'What is the difference between Installer and Portable versions?',
          a: 'The Installer automatically registers and manages the background indexing service. The Portable version runs immediately upon unzipping and allows one-click service registration inside settings if needed.'
        },
        {
          q: 'Are search queries or file data transmitted to the cloud?',
          a: 'Never. Lertaro is 100% local and offline. There is zero telemetry, analytics reporting, or cloud communication of any kind.'
        }
      ],

      commTitle: 'Open Source Community & Contributing',
      commSubtitle: 'Lertaro is free and open source. Code contributions, plugin development, and feedback are always welcome',
      commGithubTitle: 'GitHub Repository',
      commGithubDesc: 'Browse source code, release binaries, and commit history',
      commIssuesTitle: 'Feedback & Issues',
      commIssuesDesc: 'Found a bug or have a feature idea? Open an issue',
      commDevTitle: 'Plugin Developer Guide',
      commDevDesc: 'Build your custom extensions with the .NET 10 SDK',
      commDonateTitle: 'Donate & Support',
      commDonateDesc: 'If Lertaro boosts your daily workflow, consider supporting development'
    }
  }
})
</script>

<template>
  <div class="practical-container">
    <!-- Section 1: 3-Process Architecture -->
    <section class="sub-section">
      <div class="sub-header">
        <h2 class="sub-title">{{ content.archTitle }}</h2>
        <p class="sub-desc">{{ content.archSubtitle }}</p>
      </div>

      <div class="arch-triplet-grid">
        <div class="arch-card">
          <div class="arch-card-top">
            <span class="role-badge system">{{ content.proc1Role }}</span>
            <code class="proc-name">{{ content.proc1Name }}</code>
          </div>
          <p class="arch-desc">{{ content.proc1Desc }}</p>
        </div>

        <div class="arch-card">
          <div class="arch-card-top">
            <span class="role-badge user">{{ content.proc2Role }}</span>
            <code class="proc-name">{{ content.proc2Name }}</code>
          </div>
          <p class="arch-desc">{{ content.proc2Desc }}</p>
        </div>

        <div class="arch-card">
          <div class="arch-card-top">
            <span class="role-badge hook">{{ content.proc3Role }}</span>
            <code class="proc-name">{{ content.proc3Name }}</code>
          </div>
          <p class="arch-desc">{{ content.proc3Desc }}</p>
        </div>
      </div>

      <div class="arch-link-row">
        <a :href="`${localePrefix()}/dev-guide/architecture.html`" class="arch-doc-link">
          {{ content.archLink }}
        </a>
      </div>
    </section>

    <!-- Section 2: Compatibility & Ecosystem Integration -->
    <section class="sub-section">
      <div class="sub-header">
        <h2 class="sub-title">{{ content.compatTitle }}</h2>
        <p class="sub-desc">{{ content.compatSubtitle }}</p>
      </div>

      <div class="compat-triplet-grid">
        <div class="compat-card">
          <h3 class="compat-card-title">{{ content.osTitle }}</h3>
          <p class="compat-card-desc">{{ content.osDesc }}</p>
        </div>

        <div class="compat-card">
          <h3 class="compat-card-title">{{ content.fmTitle }}</h3>
          <p class="compat-card-desc">{{ content.fmDesc }}</p>
        </div>

        <div class="compat-card">
          <h3 class="compat-card-title">{{ content.toolsTitle }}</h3>
          <p class="compat-card-desc">{{ content.toolsDesc }}</p>
        </div>
      </div>
    </section>

    <!-- Section 3: Frequently Asked Questions (FAQ) -->
    <section class="sub-section">
      <div class="sub-header">
        <h2 class="sub-title">{{ content.faqTitle }}</h2>
        <p class="sub-desc">{{ content.faqSubtitle }}</p>
      </div>

      <div class="faq-grid">
        <div v-for="(item, idx) in content.faqs" :key="idx" class="faq-card">
          <div class="faq-q">
            <span class="faq-q-badge">Q</span>
            <h3 class="faq-q-text">{{ item.q }}</h3>
          </div>
          <p class="faq-a">{{ item.a }}</p>
        </div>
      </div>
    </section>

    <!-- Section 4: Community & Contributing -->
    <section class="sub-section">
      <div class="sub-header">
        <h2 class="sub-title">{{ content.commTitle }}</h2>
        <p class="sub-desc">{{ content.commSubtitle }}</p>
      </div>

      <div class="comm-grid">
        <a href="https://github.com/Lertaro/Lertaro" target="_blank" rel="noreferrer" class="comm-card">
          <div class="comm-icon">⭐</div>
          <div class="comm-info">
            <h3 class="comm-name">{{ content.commGithubTitle }}</h3>
            <p class="comm-details">{{ content.commGithubDesc }}</p>
          </div>
        </a>

        <a href="https://github.com/Lertaro/Lertaro/issues" target="_blank" rel="noreferrer" class="comm-card">
          <div class="comm-icon">💬</div>
          <div class="comm-info">
            <h3 class="comm-name">{{ content.commIssuesTitle }}</h3>
            <p class="comm-details">{{ content.commIssuesDesc }}</p>
          </div>
        </a>

        <a :href="`${localePrefix()}/dev-guide/getting-started.html`" class="comm-card">
          <div class="comm-icon">🧩</div>
          <div class="comm-info">
            <h3 class="comm-name">{{ content.commDevTitle }}</h3>
            <p class="comm-details">{{ content.commDevDesc }}</p>
          </div>
        </a>

        <a :href="`${localePrefix()}/user-guide/donate.html`" class="comm-card">
          <div class="comm-icon">☕</div>
          <div class="comm-info">
            <h3 class="comm-name">{{ content.commDonateTitle }}</h3>
            <p class="comm-details">{{ content.commDonateDesc }}</p>
          </div>
        </a>
      </div>
    </section>
  </div>
</template>

<style scoped>
.practical-container {
  max-width: 1152px;
  margin: 48px auto 60px;
  padding: 0 24px;
}

.sub-section {
  margin-bottom: 56px;
}

.sub-header {
  text-align: center;
  max-width: 720px;
  margin: 0 auto 28px;
}

.sub-title {
  font-size: 26px;
  font-weight: 800;
  color: var(--vp-c-text-1);
  margin: 0 0 8px;
  letter-spacing: -0.01em;
}

.sub-desc {
  font-size: 15px;
  color: var(--vp-c-text-2);
  margin: 0;
  line-height: 1.5;
}

/* Architecture Triplet & Compatibility Grid */
.arch-triplet-grid, .compat-triplet-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 18px;
}

.arch-card, .compat-card {
  background: var(--vp-c-bg-soft);
  border: 1px solid var(--vp-c-divider);
  border-radius: 14px;
  padding: 22px 20px;
  display: flex;
  flex-direction: column;
  transition: transform 0.2s ease, border-color 0.2s ease;
}

.arch-card:hover, .compat-card:hover {
  transform: translateY(-2px);
  border-color: var(--vp-c-brand-1);
}

.dark .arch-card, .dark .compat-card {
  background: rgba(22, 27, 34, 0.7);
  border-color: rgba(255, 255, 255, 0.08);
}

.arch-card-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
}

.role-badge {
  font-size: 11px;
  font-weight: 700;
  padding: 2px 8px;
  border-radius: 6px;
}

.role-badge.system {
  background: rgba(239, 68, 68, 0.12);
  color: #ef4444;
}

.role-badge.user {
  background: rgba(59, 130, 246, 0.12);
  color: #3b82f6;
}

.role-badge.hook {
  background: rgba(16, 185, 129, 0.12);
  color: #10b981;
}

.proc-name {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 12px;
  font-weight: 600;
  color: var(--vp-c-text-1);
}

.arch-desc, .compat-card-desc {
  font-size: 13px;
  line-height: 1.6;
  color: var(--vp-c-text-2);
  margin: 0;
  flex: 1;
}

.arch-link-row {
  margin-top: 18px;
  text-align: center;
}

.arch-doc-link {
  font-size: 13px;
  font-weight: 600;
  color: var(--vp-c-brand-1);
  text-decoration: none;
}

.arch-doc-link:hover {
  text-decoration: underline;
}

.compat-card-title {
  font-size: 15px;
  font-weight: 700;
  color: var(--vp-c-text-1);
  margin: 0 0 10px;
}

/* FAQ Grid */
.faq-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 18px;
}

.faq-card {
  background: var(--vp-c-bg-elv);
  border: 1px solid var(--vp-c-divider);
  border-radius: 14px;
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  transition: transform 0.2s ease, border-color 0.2s ease;
}

.faq-card:hover {
  transform: translateY(-2px);
  border-color: var(--vp-c-brand-1);
}

.dark .faq-card {
  background: rgba(22, 27, 34, 0.75);
  border-color: rgba(255, 255, 255, 0.08);
}

.faq-q {
  display: flex;
  align-items: flex-start;
  gap: 10px;
}

.faq-q-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  border-radius: 6px;
  background: var(--vp-c-brand-soft);
  color: var(--vp-c-brand-1);
  font-size: 12px;
  font-weight: 800;
  flex-shrink: 0;
  margin-top: 1px;
}

.faq-q-text {
  font-size: 14px;
  font-weight: 700;
  color: var(--vp-c-text-1);
  margin: 0;
  line-height: 1.45;
}

.faq-a {
  font-size: 13px;
  line-height: 1.65;
  color: var(--vp-c-text-2);
  margin: 0;
  padding-left: 32px;
}

/* Community Grid */
.comm-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 16px;
}

.comm-card {
  background: var(--vp-c-bg-soft);
  border: 1px solid var(--vp-c-divider);
  border-radius: 14px;
  padding: 18px 16px;
  display: flex;
  align-items: flex-start;
  gap: 12px;
  text-decoration: none !important;
  transition: transform 0.2s ease, border-color 0.2s ease, box-shadow 0.2s ease;
}

.comm-card:hover {
  transform: translateY(-2px);
  border-color: var(--vp-c-brand-1);
  box-shadow: 0 8px 20px -6px rgba(24, 104, 183, 0.12);
}

.dark .comm-card {
  background: rgba(22, 27, 34, 0.7);
  border-color: rgba(255, 255, 255, 0.08);
}

.comm-icon {
  font-size: 22px;
  flex-shrink: 0;
  line-height: 1;
}

.comm-info {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.comm-name {
  font-size: 14px;
  font-weight: 700;
  color: var(--vp-c-text-1);
  margin: 0;
}

.comm-details {
  font-size: 12px;
  color: var(--vp-c-text-3);
  margin: 0;
  line-height: 1.45;
}

@media (max-width: 960px) {
  .arch-triplet-grid, .compat-triplet-grid, .comm-grid {
    grid-template-columns: repeat(2, 1fr);
  }
  .faq-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 640px) {
  .arch-triplet-grid, .compat-triplet-grid, .comm-grid {
    grid-template-columns: 1fr;
  }
}
</style>
