/*
 * Drive harness for the Platform.Bible "lexicon" extension running in paranext-core (Electron).
 * Attaches to a running instance over CDP (launched with --remote-debugging-port, see launch.sh)
 * and drives it with Playwright: screenshots, DOM reads, clicks, and window.papi calls.
 *
 * Usage:  node drive.js <command> [args]
 *   dump                       list every page/frame (debug what's loaded)
 *   shoot <name>               screenshot the main window as <name>.png
 *   open [webViewType] [name]  openWebView (default Select Lexicon), then screenshot
 *   click <textRegex> [name]   click a button matching /textRegex/i in the active web-view, screenshot
 *   text                       print the active web-view's visible text + button labels
 *   signout [authority]        call lexicon.logout via papi (reset auth state; default lexbox.org)
 *
 * Env overrides: CDP_URL (http://127.0.0.1:9222), SHOT_DIR, PARANEXT_CORE (to locate Playwright).
 */
const path = require('path');
const fs = require('fs');
const os = require('os');

const REPO_ROOT = path.resolve(__dirname, '..', '..', '..');
const PARANEXT_CORE = process.env.PARANEXT_CORE || path.resolve(REPO_ROOT, '..', 'paranext-core');
// paranext-core ships Playwright; reuse it rather than adding a dep to this repo.
const playwright = require(path.join(PARANEXT_CORE, 'node_modules', 'playwright'));

const CDP = process.env.CDP_URL || 'http://127.0.0.1:9222';
const SHOT_DIR = process.env.SHOT_DIR || path.join(os.tmpdir(), 'pb-extension-shots');
const DEFAULT_WEBVIEW = 'lexicon-select-lexicon.react';
fs.mkdirSync(SHOT_DIR, { recursive: true });

const log = (...a) => console.log('[drive]', ...a);

async function connectWithRetry(timeoutMs = 300000) {
  const start = Date.now();
  let lastErr;
  while (Date.now() - start < timeoutMs) {
    try {
      return await playwright.chromium.connectOverCDP(CDP);
    } catch (e) {
      lastErr = e;
      await new Promise((r) => setTimeout(r, 2000));
    }
  }
  throw new Error(`Timed out connecting to CDP ${CDP}: ${lastErr && lastErr.message}`);
}

// The main renderer is the page exposing window.papi.webViews (not devtools/about:blank).
async function getMainPage(browser) {
  for (const ctx of browser.contexts()) {
    for (const page of ctx.pages()) {
      if (page.url().startsWith('devtools://')) continue;
      try {
        if (await page.evaluate(() => !!(window.papi && window.papi.webViews))) return page;
      } catch {
        /* not ready / cross-origin */
      }
    }
  }
  return null;
}

// If the renderer loaded before its dev server was ready it parks on a chrome-error:// page.
// Reload such pages periodically until window.papi shows up.
async function waitForMainPage(browser, timeoutMs = 180000) {
  const start = Date.now();
  let lastReload = 0;
  while (Date.now() - start < timeoutMs) {
    const p = await getMainPage(browser);
    if (p) return p;
    if (Date.now() - lastReload > 5000) {
      lastReload = Date.now();
      for (const ctx of browser.contexts()) {
        for (const page of ctx.pages()) {
          if (page.url().startsWith('chrome-error://')) {
            try {
              await page.reload({ waitUntil: 'domcontentloaded', timeout: 15000 });
            } catch {
              /* keep trying */
            }
          }
        }
      }
    }
    await new Promise((r) => setTimeout(r, 1500));
  }
  throw new Error('Timed out waiting for main window with window.papi');
}

// The extension UI renders in an about:srcdoc iframe; the freshly-opened visible tab is the last one.
function activeWebViewFrame(page) {
  const frames = page.frames().filter((f) => f.url() === 'about:srcdoc');
  return frames[frames.length - 1] || null;
}

async function shoot(page, name) {
  const file = path.join(SHOT_DIR, name.endsWith('.png') ? name : `${name}.png`);
  await page.screenshot({ path: file });
  log('screenshot ->', file);
  return file;
}

(async () => {
  const cmd = process.argv[2] || 'dump';
  const arg = process.argv[3];
  const arg2 = process.argv[4];
  const browser = await connectWithRetry();
  log('connected to', CDP);

  if (cmd === 'dump') {
    for (const ctx of browser.contexts()) {
      for (const page of ctx.pages()) {
        let papi = false;
        try {
          papi = await page.evaluate(() => !!window.papi);
        } catch {
          /* ignore */
        }
        log('page:', JSON.stringify(page.url()), `papi=${papi}`, `frames=${page.frames().length}`);
      }
    }
    await browser.close();
    return;
  }

  const page = await waitForMainPage(browser);
  log('main page:', page.url());

  if (cmd === 'shoot') {
    await page.waitForTimeout(1000);
    await shoot(page, arg || 'shot');
  } else if (cmd === 'open') {
    const webViewType = arg && arg.includes('.') ? arg : DEFAULT_WEBVIEW;
    const name = (arg && !arg.includes('.') ? arg : arg2) || '02-webview';
    const res = await page.evaluate(
      (t) =>
        window.papi.webViews
          .openWebView(t, undefined, {})
          .then((id) => ({ ok: true, id }))
          .catch((e) => ({ ok: false, error: String((e && e.message) || e) })),
      webViewType,
    );
    log('openWebView:', JSON.stringify(res));
    await page.waitForTimeout(4000);
    await shoot(page, name);
  } else if (cmd === 'click') {
    if (!arg) throw new Error('click needs a button-text regex');
    const frame = activeWebViewFrame(page);
    if (!frame) throw new Error('no about:srcdoc web-view frame found — open one first');
    const clicked = await frame.evaluate((re) => {
      const rx = new RegExp(re, 'i');
      const b = [...document.querySelectorAll('button')].find((x) => rx.test(x.textContent || ''));
      if (b) b.click();
      return !!b;
    }, arg);
    log(`clicked /${arg}/i:`, clicked);
    await page.waitForTimeout(2500);
    await shoot(page, arg2 || '03-after-click');
  } else if (cmd === 'text') {
    const frame = activeWebViewFrame(page);
    if (!frame) throw new Error('no about:srcdoc web-view frame found — open one first');
    const info = await frame.evaluate(() => ({
      text: document.body ? document.body.innerText.slice(0, 600) : '(no body)',
      buttons: [...document.querySelectorAll('button')].map((b) => (b.textContent || '').trim()),
    }));
    log('web-view:', JSON.stringify(info, null, 2));
  } else if (cmd === 'key') {
    // Press a key on the main page (e.g. Escape to dismiss a dialog).
    await page.keyboard.press(arg || 'Escape');
    await page.waitForTimeout(800);
    if (arg2) await shoot(page, arg2);
  } else if (cmd === 'pick') {
    // Click a listbox option (role=option, e.g. a cmdk CommandItem) by text in the active web-view.
    if (!arg) throw new Error('pick needs an option-text regex');
    const frame = activeWebViewFrame(page);
    if (!frame) throw new Error('no about:srcdoc web-view frame found — open one first');
    const option = frame.locator('[role="option"]', { hasText: new RegExp(arg, 'i') }).first();
    await option.click({ timeout: 10000 });
    log(`picked /${arg}/i:`, JSON.stringify(await option.textContent().catch(() => null)));
    await page.waitForTimeout(1000);
    if (arg2) await shoot(page, arg2);
  } else if (cmd === 'rightclick') {
    // Right-click a listbox option by text (opens its context menu, if any).
    if (!arg) throw new Error('rightclick needs an option-text regex');
    const frame = activeWebViewFrame(page);
    if (!frame) throw new Error('no about:srcdoc web-view frame found — open one first');
    await frame
      .locator('[role="option"]', { hasText: new RegExp(arg, 'i') })
      .first()
      .click({ button: 'right', timeout: 10000 });
    await page.waitForTimeout(1000);
    if (arg2) await shoot(page, arg2);
  } else if (cmd === 'menu') {
    // Click a context/dropdown menu item by text in the active web-view.
    if (!arg) throw new Error('menu needs an item-text regex');
    const frame = activeWebViewFrame(page);
    if (!frame) throw new Error('no about:srcdoc web-view frame found — open one first');
    await frame
      .locator('[role="menuitem"]', { hasText: new RegExp(arg, 'i') })
      .first()
      .click({ timeout: 10000 });
    await page.waitForTimeout(1000);
    if (arg2) await shoot(page, arg2);
  } else if (cmd === 'fill') {
    // Type into the first visible text input of the active web-view (e.g. the cmdk filter).
    const frame = activeWebViewFrame(page);
    if (!frame) throw new Error('no about:srcdoc web-view frame found — open one first');
    await frame.fill('input:visible', arg || '');
    await page.waitForTimeout(800);
    if (arg2) await shoot(page, arg2);
  } else if (cmd === 'eval') {
    // Run arbitrary JS in the main renderer (awaited); pass an expression, e.g.
    //   node drive.js eval "window.papi.projectLookup.getMetadataForAllProjects()"
    if (!arg) throw new Error('eval needs a JS expression');
    const res = await page.evaluate(`(async () => (${arg}))()`);
    log('eval:', JSON.stringify(res, null, 2));
  } else if (cmd === 'signout') {
    const authority = arg || 'lexbox.org';
    await page.evaluate(
      (a) => window.papi.commands.sendCommand('lexicon.logout', a).catch(() => {}),
      authority,
    );
    log('sent lexicon.logout for', authority);
  } else {
    log('unknown command:', cmd);
  }

  await browser.close();
  log('done:', cmd);
})().catch((e) => {
  console.error('[drive] ERROR', e);
  process.exit(1);
});
