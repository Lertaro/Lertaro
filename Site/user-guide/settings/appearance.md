# Appearance

Pinned above [About](./about) at the bottom of the sidebar. Two groups: **Theme Mode** and the
theme picker itself.

## Theme Mode

Three cards, click to switch:

- **Light** — a single fixed theme, filtered to light-flavored ones in the grid below.
- **Dark** — same, filtered to dark-flavored ones.
- **Follow System** (split light/dark preview) — Lertaro switches between two themes you pick —
  one for when Windows is in light mode, one for dark — instead of using a single fixed theme, and
  updates immediately whenever you toggle Windows' own setting (no restart needed).

Switching between **Light** and **Dark** jumps to whichever theme of that flavor you last had
selected (the same remembered pick **Follow System** itself uses for its light/dark pair), so
flipping back and forth doesn't lose your choice on either side.

## Interface Theme

With **Light** or **Dark** selected above, a single card grid lists just that flavor's themes. With
**Follow System** selected, the grid splits into two: **Light Theme** and **Dark Theme** — each
defaults to whichever matching-flavor theme happens to be installed first, since which themes exist
at all depends entirely on which theme plugins are installed.

Each theme is shown as a card, not just a name: a small mock-up of the quick search window (search
box plus a couple of result rows, one shown selected) rendered in that theme's own colors, so you
can see what a theme actually looks like before switching to it. The active theme's card shows a
checkmark badge.

Built-in and bundled theme plugins:

- **CoreExtensions** (built-in) — Light, Dark, Nord, Sakura, Cyberpunk.
- **AnimeThemes** (bundled, if installed and enabled) — Neon Genesis, Sakura Bloom, Weathering Blue.
- **Curated Themes** (bundled, if installed and enabled) — ten light/dark pairs: Glacier,
  Terracotta, Forest, Amethyst, Crimson, Graphite, Indigo, Mint, Champagne, and Amber.

Any other theme plugin can add more cards to the grid the same way.
