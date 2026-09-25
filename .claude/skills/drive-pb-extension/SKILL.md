---
name: drive-pb-extension
description: Launch the Platform.Bible lexicon extension inside paranext-core (Electron) and drive it programmatically — screenshot it, read its UI, click buttons, and call window.papi — via Playwright over the Chrome DevTools Protocol. Use to verify or demo extension UI/behaviour end-to-end (e.g. the Lexbox login flow) when a headless build+lint is not enough.
when_to_use: User asks to "run/launch/drive/demo the Platform.Bible extension", "screenshot the lexicon extension", "test the extension in paranext-core", "drive the app", or you need visual/behavioural proof of an extension change that a build can't give.
allowed-tools: Bash(npm:*) Bash(node:*) Bash(task:*) Bash(git:*) Bash(bash:*) Bash(ls:*) Read Grep Glob Edit
---

# Drive the Platform.Bible lexicon extension

Launch the `lexicon` extension in `paranext-core` (Electron) and drive it with
Playwright over CDP. Platform.Bible is Electron, so the `Claude_Preview` /
`claude-in-chrome` MCP tools don't apply — we attach to Electron's own renderer.

Every gotcha below cost a real, verified launch attempt (the working run took ~6
tries over ~30 min before it stuck). The scripts here encode the fixes so you
don't repeat them. **Run them in order; each is idempotent.**

## The one-time setup that actually matters

The single hardest part is getting CDP open. It requires **two dev-only edits to
`paranext-core/package.json`** — do NOT try to pass the debug port at launch time
(see why below). This is scripted:

```bash
node .claude/skills/drive-pb-extension/enable-cdp.js     # patch start:main + start:extensions
```

- **`start:main`** gets `--remote-debugging-port=9222` baked *into* the
  `electronmon` command, before `{@}`. It can't go through `{@}`/`MAIN_ARGS`:
  concurrently escapes the `=` to `\=` (verified), and Electron only accepts the
  `=` form — so the port silently never opens. The space form
  (`--remote-debugging-port 9222`) survives concurrently but Electron treats the
  value as a stray arg, so that doesn't help either.
- **`start:extensions`** is neutered (paranext-core's own bundled-extensions
  webpack can crash on startup with an enhanced-resolve `pathCache` TypeError and
  tear the whole stack down; we load our extension via `--extensions`, so its
  bundled ones aren't needed).

Both are reversible: `git -C <paranext-core> checkout package.json`.

## Prerequisites (order matters)

1. **`paranext-core` is a sibling** (`../paranext-core`) with deps installed:
   `npm --prefix ../paranext-core install`. The Electron binary must actually be
   present under `node_modules/electron/dist` (~200 MB) — an interrupted install
   leaves it empty and the app silently won't launch.
2. **Build the FW Lite backend** the extension launches: `task build-fw-lite`.
3. **Build the extension LAST**, after paranext-core's `node_modules` exists:
   `npm --prefix platform.bible-extension run build`.
   ⚠️ The web-view bundle resolves `yjs` from *paranext-core's* `node_modules`.
   Build before that's populated and `yjs` won't resolve → the web-view renders
   **blank** at runtime with no build error.

`launch.sh` preflight-checks these (Electron binary, extension built, CDP enabled)
and bails with the exact fix command; it also warns if the FwLiteWeb backend (2)
isn't bundled — the app still loads, but auth/data won't work until you run
`task build-fw-lite` and rebuild.

## 1 · Launch

```bash
# Foreground blocks; run in the background and tee logs (Electron stdout won't
# appear until exit if foregrounded).
bash .claude/skills/drive-pb-extension/launch.sh > /tmp/pb-start.log 2>&1 &
```

Override the extension/paranext-core locations with `EXT_DIST=…` /
`PARANEXT_CORE=…`. If launch dies immediately, read `/tmp/pb-start.log`.

**Worktrees:** any path override may be Windows- or unix-form — `launch.sh` normalizes it
(via `cygpath`) and hard-fails if the `--extensions` path can't be made relative. This matters
because `--extensions` must reach Electron RELATIVE; an absolute path loads nothing and the
extension **silently never activates** (see the symptom below).

**Confirm it actually loaded** (don't trust the window alone): poll the backend the extension
spawns — the fastest proof activation succeeded AND registered its servers:

```bash
until curl -s http://localhost:29348/api/auth/servers; do sleep 2; done   # JSON array = ready
```

**Symptom → cause.** Blank web-view, tabs titled `%lexicon_webViewTitle_selectLexicon%`
(unresolved), log says `Extension directory for lexicon is not known` and never prints
`Lexicon extension activating!` → the extension didn't load (bad/absolute `--extensions` path),
NOT a code bug. Note `paranext-core/dev-appdata/extensions/lexicon` is FW Lite's **data** dir
(projects, msal), not the extension code — moving it does nothing for loading.

## 2 · Drive it

[`drive.js`](drive.js) attaches over CDP (it retries while the app boots — up to
a minute+) and handles the fiddly parts. Env overrides: `CDP_URL`, `SHOT_DIR`,
`PARANEXT_CORE` (to locate Playwright).

```bash
node .claude/skills/drive-pb-extension/drive.js dump                 # list pages/frames
node .claude/skills/drive-pb-extension/drive.js shoot 01-app          # screenshot main window
node .claude/skills/drive-pb-extension/drive.js open                  # open Select Lexicon, screenshot
node .claude/skills/drive-pb-extension/drive.js text                  # print active web-view text + buttons
node .claude/skills/drive-pb-extension/drive.js click "log in" 02     # click a button by text, screenshot
node .claude/skills/drive-pb-extension/drive.js pick "sena" 03        # click a listbox option (role=option) by text
node .claude/skills/drive-pb-extension/drive.js fill "search text"    # type into the web-view's first visible input
node .claude/skills/drive-pb-extension/drive.js key Escape            # press a key on the main page
node .claude/skills/drive-pb-extension/drive.js eval "window.papi.projectLookup.getMetadataForAllProjects()"  # any JS in the main renderer
node .claude/skills/drive-pb-extension/drive.js signout lexbox.org    # reset auth state via papi
```

Non-obvious techniques it encodes (reuse if you write your own):

- **Main renderer** = the page where `window.papi && window.papi.webViews` is
  truthy (skip `devtools://` / `about:blank`).
- **Startup race:** if the renderer loads before its dev server is ready it parks
  on a `chrome-error://` page forever — poll and `page.reload()` until
  `window.papi` appears.
- **Open a web-view** without clicking chrome:
  `window.papi.webViews.openWebView('lexicon-select-lexicon.react', undefined, {})`.
  Type ids are in `platform.bible-extension/src/types/enums.ts` (`WebViewType`).
- **Extension UI lives in an `about:srcdoc` iframe**, not the top page. The
  freshly-opened, visible tab is the **last** `about:srcdoc` frame — target
  `frames[frames.length - 1]`, then `frame.evaluate()`. Screenshots go on the
  top `page`.
- **Call commands directly** for setup/teardown:
  `window.papi.commands.sendCommand('lexicon.logout', 'lexbox.org')` (command ids
  are registered in `platform.bible-extension/src/main.ts`).
- **Open a web-view scoped to a project**: pass options as the third arg —
  `openWebView(type, {type:'float', floatSize:{width:440,height:560}}, {projectId: '<id>'})`.
  Paratext project ids come from `window.papi.projectLookup.getMetadataForAllProjects()`.
- **Core dialogs** (e.g. the "Select a project" picker) render in the MAIN page, not the
  srcdoc frame; Escape does NOT close them — click their `.dock-tab-close-btn` instead.

## What the extension runs

`platform.bible-extension/src/main.ts` launches `FwLiteWeb.exe` on a fixed
`http://localhost:29348` with (among others) `--FwLiteWeb:CorsAllowAny=true`,
`--FwLiteWeb:OpenBrowser=false`, and — for login — `--Auth:SystemWebViewLogin=true`.
Login opens the user's **default browser** (system-browser OAuth); auth state is
polled from `GET /api/auth/servers`. The extension UI itself needs only the
extension + paranext-core; FW Lite is required once you exercise auth/data.

**Testing against staging** (prod config registers only `lexbox.org`): add extra servers to the
`spawn` args in `platform.bible-extension/src/main.ts`, then rebuild — mark them TEST-ONLY so they
don't ship:

```
'--Auth:LexboxServers:0:Authority=https://staging.languagedepot.org',
'--Auth:LexboxServers:0:DisplayName=Lexbox Staging',
```

**Reset a downloaded project** between test runs (no UI needed):

```bash
curl -X DELETE http://localhost:29348/api/crdt/<project-code>
```

## Teardown / relaunch gotcha: abandoned SIL mutex

Force-killing FwLiteWeb.exe or ParanextDataProvider.exe (taskkill /F — which tree teardown does)
can abandon the palaso global writing-system mutex. Symptom: after relaunch, Paratext projects
never appear (`getMetadataForAllProjects` returns only SDBG/SDBH) and main.log shows
`AbandonedMutexException` + `dotnet watch ❌ [ParanextDataProvider] Exited` — the C# provider
crash-loops on it (palaso never catches it, and any live FwLiteWeb keeps the poisoned mutex object
alive). Fix: absorb it, then touch a `.cs` file so dotnet watch restarts the provider:

```powershell
$m=[System.Threading.Mutex]::OpenExisting('C:_ProgramData_SIL_WritingSystemRepository_3')
try { $null=$m.WaitOne(0) } catch [System.Threading.AbandonedMutexException] {}
$m.ReleaseMutex(); $m.Dispose()
(Get-Item 'D:\code\paranext-core\c-sharp\Program.cs').LastWriteTime = Get-Date
```

Run the absorb step after any force-kill teardown, before trusting the next launch. Also: don't
kill FwLiteWeb.exe processes from OTHER worktrees by name — use the `fwl-down-all` skill.

## Notes

- Screenshots default to a temp dir; pass `SHOT_DIR=…` to redirect. Send them
  with `SendUserFile` — a screenshot is the proof the drive worked.
- After any code change, rebuild (`npm --prefix platform.bible-extension run build`)
  and relaunch — this launch path has no file watcher.
