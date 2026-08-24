# Appearance & Themes

Lertaro features a sleek, modern visual design system with granular theme mode controls and rich color palettes. The Appearance settings page is docked near the bottom of the left sidebar (just above "About").

## 1. Theme Modes

Three mode selection cards are displayed at the top of the page:

- **Light Mode**: Locks the interface to a bright aesthetic; the theme selector grid displays only light-themed cards.
- **Dark Mode**: Locks the interface to a sleek dark aesthetic; the grid displays only dark-themed cards.
- **Follow System**: Automatically switches between two user-specified themes as Windows toggles between light and dark modes. In this mode, the grid splits into **Light Theme** and **Dark Theme** selectors, allowing you to choose distinct palettes for day and night without requiring application restarts.

> [!NOTE]
> Lertaro independently remembers your chosen theme for both light and dark styles, preserving your selections when toggling modes.

## 2. Theme Selection Cards

Themes are presented in an interactive card grid. Each card renders an accurate mini preview of the search bar, shadow borders, typography hierarchy, and highlighted result row colors. The active theme is marked with a checkmark badge.

### Built-in & Bundled Themes

- **Core Built-in Themes (CoreExtensions)**:
  - **Light** / **Dark**: Clean and minimalistic designs that integrate seamlessly with native Windows styling.
  - **Nordic Blue**: Cool-toned geek aesthetic featuring soft ice-blue accents against a deep navy backdrop.
  - **Sakura Pink**: Gentle, refreshing pastel rose and white palette.
  - **Cyberpunk**: High-contrast neon yellow and midnight purple for a futuristic flair.
- **Anime Themes (AnimeThemes, Bundled)**:
  - **Evangelion**, **Sakura Blossom**, and **Weathering with You**.
- **Curated Theme Pairs (Curated Themes, Bundled)**:
  - 10 paired light/dark palettes: **Glacier Blue**, **Terracotta**, **Forest Green**, **Amethyst**, **Crimson**, **Graphite**, **Indigo Night**, **Mint Cyan**, **Champagne Gold**, and **Amber Gold**.

### Plugin Theme Extensibility

All themes adhere to the standard UI theme specifications provided by `Lertaro.PluginSdk`. Third-party developers can create and distribute custom theme plugins to register new palettes in this grid.
