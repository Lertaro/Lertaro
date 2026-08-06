<script setup>
import { ref, onMounted, onUnmounted } from 'vue'
import { useData } from 'vitepress'
import { dictionary } from '../i18n/dictionary.js'

const { lang } = useData()
const t = (key) => (dictionary[lang.value] ?? dictionary['en-US'])[key]
const isOpen = ref(false)
const dropdownRef = ref(null)

const toggleDropdown = () => {
  isOpen.value = !isOpen.value
}

const closeDropdown = (e) => {
  if (dropdownRef.value && !dropdownRef.value.contains(e.target)) {
    isOpen.value = false
  }
}

onMounted(() => {
  window.addEventListener('click', closeDropdown)
})

onUnmounted(() => {
  window.removeEventListener('click', closeDropdown)
})
</script>

<template>
  <div class="download-dropdown" ref="dropdownRef">
    <!-- Split Button Container -->
    <div class="split-button-container">
      <!-- Main Action (Installer Link) -->
      <a 
        class="main-action-btn"
        href="https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup.exe"
      >
        <span class="icon">💾</span>
        <span>{{ t('downloadMain') }}</span>
      </a>

      <!-- Dropdown Toggle Arrow -->
      <button 
        class="toggle-btn"
        :aria-expanded="isOpen"
        @click.stop="toggleDropdown"
      >
        <svg 
          xmlns="http://www.w3.org/2000/svg" 
          viewBox="0 0 20 20" 
          fill="currentColor"
          class="arrow-icon"
          :class="{ 'rotated': isOpen }"
        >
          <path fill-rule="evenodd" d="M5.23 7.21a.75.75 0 011.06.02L10 11.168l3.71-3.938a.75.75 0 111.08 1.04l-4.25 4.5a.75.75 0 01-1.08 0l-4.25-4.5a.75.75 0 01.02-1.06z" clip-rule="evenodd" />
        </svg>
      </button>
    </div>

    <!-- Dropdown Menu -->
    <transition name="fade-slide">
      <div v-if="isOpen" class="dropdown-menu">
        <div class="menu-section">{{ t('downloadX64Section') }}</div>

        <a
          href="https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup.exe"
          class="menu-item"
        >
          <span class="item-icon">💾</span>
          <div class="item-content">
            <span class="item-title">{{ t('downloadInstallerTitle') }}</span>
            <span class="item-desc">{{ t('downloadInstallerDesc') }}</span>
          </div>
        </a>

        <a
          href="https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable.zip"
          class="menu-item"
        >
          <span class="item-icon">📦</span>
          <div class="item-content">
            <span class="item-title">{{ t('downloadPortableTitle') }}</span>
            <span class="item-desc">{{ t('downloadPortableDesc') }}</span>
          </div>
        </a>

        <!-- The two builds differ only by architecture, so these repeat the titles above without
             repeating their descriptions: the section heading is what distinguishes them. -->
        <div class="menu-section">{{ t('downloadArmSection') }}</div>

        <a
          href="https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup_arm64.exe"
          class="menu-item compact"
        >
          <span class="item-icon">💾</span>
          <div class="item-content">
            <span class="item-title">{{ t('downloadInstallerTitle') }}</span>
          </div>
        </a>

        <a
          href="https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable_arm64.zip"
          class="menu-item compact"
        >
          <span class="item-icon">📦</span>
          <div class="item-content">
            <span class="item-title">{{ t('downloadPortableTitle') }}</span>
          </div>
        </a>
      </div>
    </transition>
  </div>
</template>

<style scoped>
.download-dropdown {
  position: relative;
  display: inline-block;
  vertical-align: middle;
}

.split-button-container {
  display: inline-flex;
  align-items: stretch;
  border-radius: 20px;
  overflow: hidden;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
  transition: box-shadow 0.25s ease;
}

.split-button-container:hover {
  box-shadow: 0 6px 18px rgba(0, 0, 0, 0.15);
}

.main-action-btn, .toggle-btn {
  background-color: var(--vp-c-brand-1, #3eaf7c);
  color: var(--vp-c-neutral-inverse, #ffffff) !important;
  font-size: 14px;
  font-weight: 600;
  border: none;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  transition: background-color 0.2s ease;
}

.main-action-btn {
  padding: 0 20px;
  height: 40px;
  text-decoration: none !important;
}

.main-action-btn .icon {
  margin-right: 6px;
  font-size: 15px;
}

.toggle-btn {
  padding: 0 10px;
  border-left: 1px solid rgba(255, 255, 255, 0.25);
  height: 40px;
}

.main-action-btn:hover, .toggle-btn:hover {
  background-color: var(--vp-c-brand-2, #3a9f70);
}

.arrow-icon {
  width: 18px;
  height: 18px;
  transition: transform 0.25s ease;
}

.arrow-icon.rotated {
  transform: rotate(180deg);
}

/* Dropdown Menu styling */
.dropdown-menu {
  position: absolute;
  top: calc(100% + 8px);
  left: 0;
  z-index: 100;
  width: 280px;
  background-color: var(--vp-c-bg-elv, #ffffff);
  border: 1px solid var(--vp-c-divider, #e2e8f0);
  border-radius: 12px;
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.15);
  padding: 6px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.menu-section {
  font-size: 11px;
  font-weight: 700;
  color: var(--vp-c-text-3, #999999);
  padding: 6px 12px 2px;
  line-height: 1.3;
}

/* The first heading sits right under the menu's own padding, so it does not need its own. */
.menu-section:first-child {
  padding-top: 2px;
}

.menu-item {
  display: flex;
  align-items: flex-start;
  padding: 8px 12px;
  border-radius: 8px;
  text-decoration: none !important;
  color: var(--vp-c-text-1, #2c3e50) !important;
  transition: background-color 0.15s ease;
}

.menu-item:hover {
  background-color: var(--vp-c-bg-soft, #f6f6f7);
}

.item-icon {
  font-size: 18px;
  margin-right: 12px;
  margin-top: 2px;
}

/* Used by the entries that carry a title only, so the icon centres instead of hanging. */
.menu-item.compact {
  padding: 6px 12px;
  align-items: center;
}

.menu-item.compact .item-icon {
  font-size: 15px;
  margin-top: 0;
}

.item-content {
  display: flex;
  flex-direction: column;
  text-align: left;
}

.item-title {
  font-weight: 700;
  font-size: 13.5px;
  line-height: 1.4;
}

.item-desc {
  font-size: 11px;
  color: var(--vp-c-text-2, #888888);
  margin-top: 1px;
  line-height: 1.3;
}

/* Vue Transitions */
.fade-slide-enter-active, .fade-slide-leave-active {
  transition: opacity 0.2s ease, transform 0.2s ease;
}

.fade-slide-enter-from, .fade-slide-leave-to {
  opacity: 0;
  transform: translateY(-8px);
}
</style>
