import { defineConfig } from 'vitepress'
import { dictionary } from './i18n/dictionary.js'
import { buildLocaleConfig } from './i18n/buildLocaleConfig.js'

// config.mjs stays a thin shell: all nav/sidebar structure comes from navSchema.js, all text comes
// from dictionary.js, and buildLocaleConfig.js glues the two together per locale. No literal
// duplicated nav/sidebar text lives in this file.
const en = buildLocaleConfig('/', dictionary['en-US'])
const zh = buildLocaleConfig('/zh-CN/', dictionary['zh-CN'])
const zhHK = buildLocaleConfig('/zh-HK/', dictionary['zh-HK'])
const zhTW = buildLocaleConfig('/zh-TW/', dictionary['zh-TW'])
const ja = buildLocaleConfig('/ja-JP/', dictionary['ja-JP'])
const ko = buildLocaleConfig('/ko-KR/', dictionary['ko-KR'])
const es = buildLocaleConfig('/es-ES/', dictionary['es-ES'])

function themeConfigFor(locale, built) {
  const t = dictionary[locale]
  return {
    nav: built.nav,
    sidebar: built.sidebar,
    socialLinks: [{ icon: 'github', link: 'https://github.com/Lertaro/Lertaro' }],
    outline: { label: t.outlineLabel },
    docFooter: { prev: t.docFooterPrev, next: t.docFooterNext },
    sidebarMenuLabel: t.sidebarMenuLabel,
    returnToTopLabel: t.returnToTopLabel,
    darkModeSwitchLabel: t.darkModeSwitchLabel,
    lightModeSwitchTitle: t.lightModeSwitchTitle,
    darkModeSwitchTitle: t.darkModeSwitchTitle,
    lastUpdated: { text: t.lastUpdatedText },
  }
}

function ogHeadFor(title, description) {
  return [
    ['meta', { property: 'og:title', content: title }],
    ['meta', { property: 'og:description', content: description }],
  ]
}

function searchTranslationsFor(locale) {
  const t = dictionary[locale]
  return {
    translations: {
      button: { buttonText: t.searchButtonText, buttonAriaLabel: t.searchButtonAriaLabel },
      modal: {
        displayDetails: t.searchDisplayDetails,
        resetButtonTitle: t.searchResetButtonTitle,
        backButtonTitle: t.searchBackButtonTitle,
        noResultsText: t.searchNoResultsText,
        footer: {
          selectText: t.searchFooterSelectText,
          navigateText: t.searchFooterNavigateText,
          closeText: t.searchFooterCloseText,
        },
      },
    },
  }
}

export default defineConfig({
  title: 'Lertaro',
  description: 'High-performance, extensible search utility for Windows / 高性能、可扩展的 Windows 全局检索系统',
  lastUpdated: true,
  // Local search must be enabled once at the root themeConfig (not per-locale, unlike nav/sidebar/
  // etc.) -- VitePress only renders the search UI when it sees this at the top level. Per-locale
  // translations still come from dictionary.js, just nested under options.locales instead.
  themeConfig: {
    search: {
      provider: 'local',
      options: {
        locales: {
          root: searchTranslationsFor('en-US'),
          'zh-CN': searchTranslationsFor('zh-CN'),
          'zh-HK': searchTranslationsFor('zh-HK'),
          'zh-TW': searchTranslationsFor('zh-TW'),
          'ja-JP': searchTranslationsFor('ja-JP'),
          'ko-KR': searchTranslationsFor('ko-KR'),
          'es-ES': searchTranslationsFor('es-ES'),
        },
      },
    },
  },
  head: [
    ['link', { rel: 'icon', href: '/favicon.ico' }],
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { property: 'og:image', content: 'https://lertaro.github.io/logo.png' }],
    ['meta', { name: 'twitter:card', content: 'summary' }],
    [
      'script',
      {},
      `
      (function() {
        var lang = navigator.language || navigator.userLanguage;
        // If browser language is Chinese and we are at the root homepage, redirect to the Chinese site /zh-CN/
        if (lang && lang.indexOf('zh') === 0 && (window.location.pathname === '/' || window.location.pathname === '/index.html')) {
          window.location.pathname = '/zh-CN/';
        }
      })();
      `,
    ],
  ],
  locales: {
    root: {
      label: 'English',
      lang: 'en-US',
      title: 'Lertaro',
      description: 'High-performance, extensible search utility for Windows',
      head: ogHeadFor('Lertaro', 'High-performance, extensible search utility for Windows'),
      themeConfig: themeConfigFor('en-US', en),
    },
    'zh-CN': {
      label: '简体中文',
      lang: 'zh-CN',
      link: '/zh-CN/',
      title: 'Lertaro',
      description: '高性能、可扩展的 Windows 全局检索系统',
      head: ogHeadFor('Lertaro', '高性能、可扩展的 Windows 全局检索系统'),
      themeConfig: themeConfigFor('zh-CN', zh),
    },
    'zh-HK': {
      label: '繁體中文（香港）',
      lang: 'zh-HK',
      link: '/zh-HK/',
      title: 'Lertaro',
      description: '高性能、可擴展的 Windows 檢索系統',
      head: ogHeadFor('Lertaro', '高性能、可擴展的 Windows 檢索系統'),
      themeConfig: themeConfigFor('zh-HK', zhHK),
    },
    'zh-TW': {
      label: '繁體中文（台灣）',
      lang: 'zh-TW',
      link: '/zh-TW/',
      title: 'Lertaro',
      description: '高效能、可擴充的 Windows 搜尋系統',
      head: ogHeadFor('Lertaro', '高效能、可擴充的 Windows 搜尋系統'),
      themeConfig: themeConfigFor('zh-TW', zhTW),
    },
    'ja-JP': {
      label: '日本語',
      lang: 'ja-JP',
      link: '/ja-JP/',
      title: 'Lertaro',
      description: 'Windows 向けの高性能で拡張可能な検索ツール',
      head: ogHeadFor('Lertaro', 'Windows 向けの高性能で拡張可能な検索ツール'),
      themeConfig: themeConfigFor('ja-JP', ja),
    },
    'ko-KR': {
      label: '한국어',
      lang: 'ko-KR',
      link: '/ko-KR/',
      title: 'Lertaro',
      description: 'Windows용 고성능 확장형 검색 도구',
      head: ogHeadFor('Lertaro', 'Windows용 고성능 확장형 검색 도구'),
      themeConfig: themeConfigFor('ko-KR', ko),
    },
    'es-ES': {
      label: 'Español',
      lang: 'es-ES',
      link: '/es-ES/',
      title: 'Lertaro',
      description: 'Búsqueda extensible de alto rendimiento para Windows',
      head: ogHeadFor('Lertaro', 'Búsqueda extensible de alto rendimiento para Windows'),
      themeConfig: themeConfigFor('es-ES', es),
    },
  },
})
