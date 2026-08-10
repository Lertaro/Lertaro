<script setup>
import { useData } from 'vitepress'
import DefaultTheme from 'vitepress/theme'
import DownloadDropdown from './DownloadDropdown.vue'
import { dictionary } from '../i18n/dictionary.js'

const { Layout } = DefaultTheme
const { lang, frontmatter } = useData()
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

    <template #home-hero-before>
      <aside v-if="frontmatter.securityWarning" class="security-warning" role="alert">
        <span class="security-warning-icon" aria-hidden="true">!</span>
        <div class="security-warning-copy">
          <strong>{{ frontmatter.securityWarning.title }}</strong>
          <p>{{ frontmatter.securityWarning.details }}</p>
        </div>
        <div class="security-warning-links">
          <a href="https://github.com/Lertaro/Lertaro">github.com/Lertaro/Lertaro</a>
          <a href="https://lertaro.github.io/">lertaro.github.io</a>
          <a href="https://github.com/Lertaro/Lertaro/releases">GitHub Releases</a>
        </div>
      </aside>
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

.security-warning {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: center;
  gap: 14px;
  width: min(1152px, calc(100% - 48px));
  margin: 24px auto 0;
  padding: 13px 16px;
  border: 1px solid rgba(220, 38, 38, 0.3);
  border-radius: 14px;
  background: linear-gradient(110deg, rgba(220, 38, 38, 0.09), rgba(245, 158, 11, 0.07));
  box-shadow: 0 10px 30px rgba(127, 29, 29, 0.07);
  color: var(--vp-c-text-1);
}

.security-warning-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 30px;
  height: 30px;
  border-radius: 50%;
  background: #dc2626;
  color: #fff;
  font-size: 19px;
  font-weight: 800;
  line-height: 1;
}

.security-warning-copy strong {
  color: #b91c1c;
  font-size: 15px;
}

.dark .security-warning-copy strong {
  color: #fca5a5;
}

.security-warning-copy p {
  margin: 3px 0 0;
  color: var(--vp-c-text-2);
  font-size: 13px;
  line-height: 1.55;
}

.security-warning-links {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 6px;
  max-width: 360px;
  font-size: 12px;
  font-weight: 600;
}

.security-warning-links a {
  padding: 5px 9px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 999px;
  background: var(--vp-c-bg);
  color: var(--vp-c-text-1);
  text-decoration: none;
  transition: border-color 0.2s ease, color 0.2s ease;
}

.security-warning-links a:hover {
  border-color: var(--vp-c-brand-1);
  color: var(--vp-c-brand-1);
}

@media (max-width: 959px) {
  .security-warning {
    grid-template-columns: auto minmax(0, 1fr);
    width: min(100% - 32px, 688px);
    margin-top: 16px;
  }

  .security-warning-links {
    grid-column: 2;
    justify-content: flex-start;
    max-width: none;
  }
}

@media (max-width: 520px) {
  .security-warning {
    align-items: start;
    padding: 12px;
  }

  .security-warning-links a {
    padding: 4px 8px;
  }
}
</style>
