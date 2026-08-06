// Locale-agnostic site structure: page ids + slugs + nesting only, no language strings.
// dictionary.js supplies the label text for every id here, keyed by locale.
// Adding a page = one node here + one key per locale in dictionary.js — nothing else to touch.
export const navSchema = [
  {
    id: 'userGuide',
    slug: 'user-guide/',
    children: [
      { id: 'ugGettingStarted', slug: 'user-guide/getting-started' },
      { id: 'ugSearchSyntax', slug: 'user-guide/search-syntax' },
      { id: 'ugHotkeys', slug: 'user-guide/hotkeys' },
      { id: 'ugActionsPreview', slug: 'user-guide/actions-and-preview' },
      { id: 'ugInstantAnswers', slug: 'user-guide/instant-answers' },
      { id: 'ugCli', slug: 'user-guide/cli' },
      { id: 'ugUriProtocol', slug: 'user-guide/uri-protocol' },
      { id: 'ugFileManagerSupport', slug: 'user-guide/file-manager-support' },
      {
        id: 'ugSettingsGroup',
        children: [
          { id: 'ugSettingsOverview', slug: 'user-guide/settings/' },
          { id: 'ugSettingsGeneral', slug: 'user-guide/settings/general' },
          { id: 'ugSettingsIndexDrives', slug: 'user-guide/settings/index-drives' },
          { id: 'ugSettingsHotkeysPage', slug: 'user-guide/settings/hotkeys-page' },
          { id: 'ugSettingsFavorites', slug: 'user-guide/settings/favorites' },
          { id: 'ugSettingsHistory', slug: 'user-guide/settings/history' },
          { id: 'ugSettingsQuickPanel', slug: 'user-guide/settings/quick-panel' },
          { id: 'ugSettingsPlugins', slug: 'user-guide/settings/plugins' },
          { id: 'ugSettingsLocalSend', slug: 'user-guide/settings/localsend' },
          { id: 'ugSettingsServiceStatus', slug: 'user-guide/settings/service-status' },
          { id: 'ugSettingsAppearance', slug: 'user-guide/settings/appearance' },
          { id: 'ugSettingsAbout', slug: 'user-guide/settings/about' },
        ],
      },
      { id: 'ugTroubleshooting', slug: 'user-guide/troubleshooting' },
      { id: 'ugDonate', slug: 'user-guide/donate' },
    ],
  },
  {
    id: 'devGuide',
    slug: 'dev-guide/',
    children: [
      { id: 'dgArchitecture', slug: 'dev-guide/architecture' },
      { id: 'dgGettingStarted', slug: 'dev-guide/getting-started' },
      {
        id: 'dgSdkGroup',
        children: [
          { id: 'dgSdkCore', slug: 'dev-guide/sdk/core-search-actions' },
          { id: 'dgSdkSystem', slug: 'dev-guide/sdk/system-adapters' },
          { id: 'dgSdkUi', slug: 'dev-guide/sdk/ui-extensions' },
          { id: 'dgSdkAbstractions', slug: 'dev-guide/sdk/abstractions' },
          { id: 'dgSdkServices', slug: 'dev-guide/sdk/services' },
        ],
      },
      { id: 'dgExamples', slug: 'dev-guide/examples' },
      { id: 'dgPackaging', slug: 'dev-guide/packaging' },
    ],
  },
]
