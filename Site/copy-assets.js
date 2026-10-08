const fs = require('fs');
const path = require('path');

const srcLogo = path.join(__dirname, '../logo.webp');

// VitePress only ever serves static assets from <root>/public (config.srcDir + 'public'), not
// .vitepress/public -- but that directory is entirely gitignored (it's a build-time copy target),
// so tracked assets live under .vitepress/public/ and are mirrored into public/ here.
// The root logo.webp is the sole source of website branding.
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
    fs.copyFileSync(srcLogo, path.join(dir, 'logo.webp'));
    for (const retired of ['logo.png', 'favicon.ico', 'architecture.svg', 'architecture-zh-CN.svg']) {
      fs.rmSync(path.join(dir, retired), { force: true });
    }
  });

  for (const file of fs.readdirSync(trackedPublicDir)) {
    if (file === 'logo.webp') continue; // already handled above
    fs.copyFileSync(path.join(trackedPublicDir, file), path.join(servedPublicDir, file));
  }

  console.log('[copy-assets] Synchronized logo.webp and public assets.');
} catch (err) {
  console.error('[copy-assets] Failed to synchronize assets:', err.message);
  process.exit(1);
}
