const fs = require('fs');
const path = require('path');

const srcLogo = path.join(__dirname, '../logo.svg');

// Copy tracked assets from .vitepress/public into VitePress's generated public directory.
// The repository-root logo.svg is the source for both logo copies.
const trackedPublicDir = path.join(__dirname, '.vitepress/public');
const servedPublicDir = path.join(__dirname, 'public');

const dests = [trackedPublicDir, servedPublicDir];

// Ensure destinations exist
dests.forEach(dir => {
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
});

// Copy assets
try {
  dests.forEach(dir => {
    fs.copyFileSync(srcLogo, path.join(dir, 'logo.svg'));
    for (const retired of ['logo.webp', 'logo.png', 'favicon.ico', 'architecture.svg', 'architecture-zh-CN.svg']) {
      fs.rmSync(path.join(dir, retired), { force: true });
    }
  });

  for (const file of fs.readdirSync(trackedPublicDir)) {
    if (file === 'logo.svg') continue; // already handled above
    fs.copyFileSync(path.join(trackedPublicDir, file), path.join(servedPublicDir, file));
  }

  console.log('[copy-assets] Synchronized logo.svg and public assets.');
} catch (err) {
  console.error('[copy-assets] Failed to synchronize assets:', err.message);
  process.exit(1);
}
