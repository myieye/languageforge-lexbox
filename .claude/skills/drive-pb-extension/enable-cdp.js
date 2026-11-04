/*
 * Idempotently patch paranext-core's package.json so the Electron app can be driven over CDP.
 * This is the crux of launching the extension for automation and the step most likely to be
 * fumbled by hand — hence a script. Both edits are dev-only and reverted with one git command.
 *
 *   1. start:main  — inject `--remote-debugging-port=<port>` INTO the electronmon command, before
 *      the `{@}` passthrough. It cannot go through `{@}` (or MAIN_ARGS): concurrently escapes the
 *      `=` to `\=`, and Electron only accepts the `=` form, so CDP never opens.
 *   2. start:extensions — neuter paranext-core's own bundled-extensions webpack build, which can
 *      crash on startup (enhanced-resolve `pathCache` TypeError) and tear the whole stack down.
 *      We load our extension via --extensions, so paranext's bundled ones aren't needed here.
 *
 * Usage:  node enable-cdp.js [port]        (default 9222; PARANEXT_CORE overrides the location)
 * Revert: git -C <paranext-core> checkout package.json
 *
 * Targeted string replacement (not JSON round-trip) to avoid reformatting the whole file.
 */
const fs = require('fs');
const path = require('path');

const REPO_ROOT = path.resolve(__dirname, '..', '..', '..');
const PARANEXT_CORE = process.env.PARANEXT_CORE || path.resolve(REPO_ROOT, '..', 'paranext-core');
const port = process.argv[2] || '9222';
const file = path.join(PARANEXT_CORE, 'package.json');

let txt = fs.readFileSync(file, 'utf8');
const changed = [];

if (!txt.includes('remote-debugging-port')) {
  const next = txt.replace('electronmon . {@}', `electronmon . --remote-debugging-port=${port} {@}`);
  if (next === txt) throw new Error('Could not find "electronmon . {@}" in start:main — paranext-core layout changed?');
  txt = next;
  changed.push(`start:main (+ --remote-debugging-port=${port})`);
}

if (txt.includes('"start:extensions": "cd extensions && npm run watch"')) {
  txt = txt.replace(
    '"start:extensions": "cd extensions && npm run watch"',
    '"start:extensions": "node -e \\"setInterval(function(){}, 1073741824)\\""',
  );
  changed.push('start:extensions (neutered to avoid startup teardown)');
}

if (changed.length) {
  fs.writeFileSync(file, txt);
  console.log('[enable-cdp] patched:', changed.join('; '));
} else {
  console.log('[enable-cdp] already enabled; no change');
}
console.log('[enable-cdp] paranext-core:', PARANEXT_CORE);
console.log('[enable-cdp] revert with:  git -C', PARANEXT_CORE, 'checkout package.json');
