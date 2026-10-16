import DefaultTheme from 'vitepress/theme'
import MyLayout from './MyLayout.vue'
import DownloadDropdown from './DownloadDropdown.vue'
import ArchitectureDiagram from './ArchitectureDiagram.vue'
import './custom.css'

export default {
  extends: DefaultTheme,
  Layout: MyLayout,
  enhanceApp({ app }) {
    app.component('DownloadDropdown', DownloadDropdown)
    app.component('ArchitectureDiagram', ArchitectureDiagram)
  }
}
