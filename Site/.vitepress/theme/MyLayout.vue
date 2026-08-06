<script setup>
import { useData } from 'vitepress'
import DefaultTheme from 'vitepress/theme'
import DownloadDropdown from './DownloadDropdown.vue'
import { dictionary } from '../i18n/dictionary.js'

const { Layout } = DefaultTheme
const { lang } = useData()
const t = (key) => (dictionary[lang.value] ?? dictionary['en-US'])[key]
const localePrefix = () => (lang.value === 'zh-CN' ? '/zh-CN' : '')
</script>

<template>
  <Layout>
    <template #home-hero-actions-after>
      <div class="custom-actions-container">
        <!-- 1. Download Dropdown Button -->
        <DownloadDropdown />

        <!-- 2. User Manual -->
        <a
          class="custom-action-btn alt-btn"
          :href="`${localePrefix()}/user-guide/getting-started.html`"
        >
          {{ t('btnGetStarted') }}
        </a>

        <!-- 3. Developer Manual -->
        <a
          class="custom-action-btn alt-btn"
          :href="`${localePrefix()}/dev-guide/getting-started.html`"
        >
          {{ t('btnDevGuide') }}
        </a>
      </div>
    </template>
  </Layout>
</template>

<style scoped>
.custom-actions-container {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  align-items: center;
  gap: 12px;
  margin-top: 24px;
}

/* Align to left on larger screens, matching VitePress default layout */
@media (min-width: 960px) {
  .custom-actions-container {
    justify-content: flex-start;
  }
}

.custom-action-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 20px;
  padding: 0 20px;
  height: 40px;
  font-size: 14px;
  font-weight: 600;
  text-decoration: none !important;
  transition: background-color 0.2s ease, color 0.2s ease, border-color 0.2s ease;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.03);
}

.alt-btn {
  background-color: var(--vp-button-alt-bg, var(--vp-c-bg-mute, #f1f5f9));
  color: var(--vp-button-alt-text, var(--vp-c-text-1, #0f172a)) !important;
  border: 1px solid var(--vp-button-alt-border, var(--vp-c-divider, #e2e8f0));
}

.alt-btn:hover {
  background-color: var(--vp-button-alt-hover-bg, var(--vp-c-bg-soft, #e2e8f0));
  color: var(--vp-button-alt-hover-text, var(--vp-c-brand-1, #3eaf7c)) !important;
  border-color: var(--vp-button-alt-hover-border, var(--vp-c-brand-1, #3eaf7c));
}
</style>
