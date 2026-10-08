<script setup>
import { computed } from 'vue'
import { useData } from 'vitepress'

const { lang } = useData()
const copy = {
  en: {
    title: 'Runtime processes and communication', cli: 'Optional command-line client',
    cliPipe: 'Per-user, per-session search pipe', app: 'Desktop application', user: 'Standard user · WPF',
    appWork: 'Search windows, settings, previews and plugin search providers',
    service: 'Indexing service', serviceRole: 'LocalSystem · Windows service',
    serviceWork: 'NTFS / ReFS metadata, FAT / exFAT watchers and network directory caches',
    query: 'Search requests ↔ results', hook: 'Hooks and window adapters',
    hookRole: 'Interactive user session', hookWork: 'Keyboard / mouse hooks, active paths and file-dialog integration',
    events: 'Window events ↔ commands', launch: 'Service launches the Hook for the signed-in session.',
    elevation: 'Hook elevation is available only for administrator accounts.',
    shared: 'Shared libraries — loaded into processes, not separate services',
    core: 'Index snapshots, fuzzy matching and IPC contracts',
    sdk: 'Search / UI in App · adapters in Hook · aliases / translations also in Service',
  },
  'zh-CN': {
    title: '运行进程与通信关系', cli: '可选命令行客户端', cliPipe: '按用户与会话隔离的搜索管道',
    app: '桌面应用', user: '标准用户 · WPF', appWork: '搜索窗口、设置、预览与插件搜索源',
    service: '索引服务', serviceRole: 'LocalSystem · Windows 服务',
    serviceWork: 'NTFS / ReFS 元数据、FAT / exFAT 监听与网络目录缓存',
    query: '搜索请求 ↔ 结果', hook: '钩子与窗口适配', hookRole: '交互用户会话',
    hookWork: '键盘与鼠标钩子、活动路径采集、文件对话框集成', events: '窗口事件 ↔ 命令',
    launch: 'Service 为已登录的用户会话启动 Hook。', elevation: '仅管理员账户可提权运行 Hook。',
    shared: '共享类库：加载到进程中，并非额外服务', core: '索引快照、模糊匹配与 IPC 契约',
    sdk: 'App：搜索与界面；Hook：窗口适配；Service：同时加载别名与翻译',
  },
  'zh-TW': {
    title: '執行程序與通訊關係', cli: '選用命令列用戶端', cliPipe: '依使用者與工作階段隔離的搜尋管道',
    app: '桌面應用程式', user: '標準使用者 · WPF', appWork: '搜尋視窗、設定、預覽與外掛搜尋來源',
    service: '索引服務', serviceRole: 'LocalSystem · Windows 服務',
    serviceWork: 'NTFS / ReFS 中繼資料、FAT / exFAT 監聽與網路目錄快取',
    query: '搜尋請求 ↔ 結果', hook: '掛鉤與視窗配接', hookRole: '互動使用者工作階段',
    hookWork: '鍵盤與滑鼠掛鉤、作用中路徑擷取、檔案對話方塊整合', events: '視窗事件 ↔ 命令',
    launch: 'Service 為已登入的使用者工作階段啟動 Hook。', elevation: '僅系統管理員帳戶可提升權限執行 Hook。',
    shared: '共用類別庫：載入程序中，並非額外服務', core: '索引快照、模糊比對與 IPC 契約',
    sdk: 'App：搜尋與介面；Hook：視窗配接；Service：同時載入別名與翻譯',
  },
}
const text = computed(() => copy[lang.value === 'zh-HK' ? 'zh-TW' : lang.value] ?? copy.en)
</script>

<template>
  <figure class="architecture" :aria-label="text.title">
    <figcaption>{{ text.title }}</figcaption>
    <div class="cli">
      <code>lff.exe</code>
      <span>{{ text.cli }}</span>
    </div>
    <div class="channel">
      <span class="arrow" aria-hidden="true">↕</span>
      <span>{{ text.cliPipe }}</span>
    </div>
    <div class="process app">
      <div class="process-title"><img src="/logo.webp" width="28" height="28" alt=""/><strong>{{ text.app }}</strong></div>
      <code>Lertaro.App.exe</code>
      <span class="identity">{{ text.user }}</span>
      <p>{{ text.appWork }}</p>
    </div>
    <div class="branches">
      <div class="branch">
        <div class="channel">
          <span class="arrow" aria-hidden="true">↕</span>
          <span>App ↔ Service</span>
          <code>LertaroPipe</code>
          <span>{{ text.query }}</span>
        </div>
        <div class="process">
          <strong>{{ text.service }}</strong>
          <code>Lertaro.Service.exe --service</code>
          <span class="identity">{{ text.serviceRole }}</span>
          <p>{{ text.serviceWork }}</p>
        </div>
      </div>
      <div class="branch">
        <div class="channel">
          <span class="arrow" aria-hidden="true">↕</span>
          <span>App ↔ Hook</span>
          <code>Lertaro_Hook_Events / Cmds</code>
          <span>{{ text.events }}</span>
        </div>
        <div class="process">
          <strong>{{ text.hook }}</strong>
          <code>Lertaro.Service.exe --hook</code>
          <span class="identity">{{ text.hookRole }}</span>
          <p>{{ text.hookWork }}</p>
        </div>
      </div>
    </div>
    <p class="launch"><span aria-hidden="true">Service → Hook</span><span>{{ text.launch }} {{ text.elevation }}</span></p>
    <div class="libraries">
      <strong>{{ text.shared }}</strong>
      <p><code>Lertaro.Core</code><span>{{ text.core }}</span></p>
      <p><code>Lertaro.PluginSdk</code><span>{{ text.sdk }}</span></p>
    </div>
  </figure>
</template>

<style scoped>
.architecture { margin: 28px 0; color: var(--vp-c-text-1); }
figcaption { margin-bottom: 20px; font-size: 17px; font-weight: 600; }
.cli { display: flex; flex-wrap: wrap; justify-content: center; gap: 6px 14px; font-size: 13px; }
.process { display: flex; flex-direction: column; align-items: flex-start; gap: 8px; padding: 18px; border: 1px solid var(--vp-c-divider); border-radius: 12px; background: var(--vp-c-bg-soft); }
.process strong { font-size: 16px; }
.process-title { display: flex; align-items: center; gap: 10px; }
.app { border-color: var(--vp-c-brand-1); background: var(--vp-c-brand-soft); }
.process p { margin: 0; font-size: 14px; line-height: 1.65; }
.identity { color: var(--vp-c-text-2); font-size: 13px; }
.branches { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 18px; }
.branch { display: flex; flex-direction: column; min-width: 0; }
.branch .process { flex: 1; }
.channel { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 3px; padding: 10px 0; color: var(--vp-c-text-2); font-size: 12px; text-align: center; }
.arrow { color: var(--vp-c-brand-1); font-size: 26px; line-height: 1.2; }
.architecture code { padding: 2px 5px; color: var(--vp-c-text-1); background: var(--vp-c-bg); font-size: 12px; overflow-wrap: anywhere; }
.launch { display: flex; align-items: baseline; gap: 12px; margin: 16px 0; color: var(--vp-c-text-2); font-size: 12px; line-height: 1.7; }
.launch > span:first-child { flex-shrink: 0; font-weight: 600; }
.libraries { padding-top: 16px; border-top: 1px solid var(--vp-c-divider); font-size: 13px; }
.libraries > strong { font-weight: 500; }
.libraries p { display: flex; align-items: baseline; gap: 10px; margin: 10px 0 0; line-height: 1.7; }
.libraries code { flex-shrink: 0; }
.libraries span { color: var(--vp-c-text-2); }
@media (max-width: 540px) {
  .branches { grid-template-columns: minmax(0, 1fr); gap: 0; }
  .process { padding: 16px; }
  .launch, .libraries p { flex-direction: column; gap: 4px; }
}
</style>
