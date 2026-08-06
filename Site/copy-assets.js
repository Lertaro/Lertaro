const fs = require('fs');
const path = require('path');

const srcLogoPng = path.join(__dirname, '../App/logo.png');
const srcLogoIco = path.join(__dirname, '../App/logo.ico');

// VitePress only ever serves static assets from <root>/public (config.srcDir + 'public'), not
// .vitepress/public -- but that directory is entirely gitignored (it's a build-time copy target),
// so tracked assets like the architecture diagrams live under .vitepress/public/ as the checked-in
// source of truth and get mirrored into public/ here, the same way logo.png/favicon.ico are
// mirrored in from App/.
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
    fs.copyFileSync(srcLogoPng, path.join(dir, 'logo.png'));
    fs.copyFileSync(srcLogoIco, path.join(dir, 'favicon.ico'));
  });

  for (const file of fs.readdirSync(trackedPublicDir)) {
    if (file === 'logo.png' || file === 'favicon.ico') continue; // already handled above
    fs.copyFileSync(path.join(trackedPublicDir, file), path.join(servedPublicDir, file));
  }

  console.log('[copy-assets] Successfully synchronized logo.png, favicon.ico, and tracked public assets.');
} catch (err) {
  console.error('[copy-assets] Failed to synchronize assets:', err.message);
  process.exit(1);
}
