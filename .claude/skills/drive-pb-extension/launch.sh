#!/usr/bin/env bash
# Launch paranext-core (Electron) with the lexicon extension loaded, ready to drive over CDP.
#
# Run `node enable-cdp.js` ONCE first — it bakes the CDP port into paranext-core's start:main.
# The port cannot ride in MAIN_ARGS: it would go through concurrently's `{@}`, which escapes the
# `=` to `\=`, and Electron only honours the `=` form (see enable-cdp.js). MAIN_ARGS carries only
# --extensions, whose path has no `=` to mangle — but it MUST be RELATIVE and colon-free: an
# absolute "D:/…" gets turned into "D\:/…" by the spawn layer. paranext-core resolves it via
# path.resolve() against its own cwd, so ../<repo>/… works.
#
# Override PARANEXT_CORE / EXT_DIST as needed. Runs in the foreground; background it and tee logs.
set -e
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SKILL_DIR/../../.." && pwd)"
PARANEXT_CORE="${PARANEXT_CORE:-$REPO_ROOT/../paranext-core}"
EXT_DIST_ABS="${EXT_DIST:-$REPO_ROOT/platform.bible-extension/dist}"

fail() { echo "PREFLIGHT FAIL: $1" >&2; exit 1; }

# Normalize any Windows-form override (D:\… or D:/…) to the shell's unix form. realpath below can
# only relativize paths sharing a root form as REPO_ROOT; a mismatch makes it fall back to an
# ABSOLUTE path, which the spawn layer can't load, so the extension SILENTLY never loads.
for _v in PARANEXT_CORE EXT_DIST_ABS; do
  case "${!_v}" in
    [A-Za-z]:[\\/]*) command -v cygpath >/dev/null 2>&1 && printf -v "$_v" '%s' "$(cygpath -u "${!_v}")" ;;
  esac
done

# 1. Electron binary actually installed (an interrupted install leaves an empty dist → silent no-launch).
ls -A "$PARANEXT_CORE/node_modules/electron/dist" >/dev/null 2>&1 \
  || fail "Electron binary missing in $PARANEXT_CORE/node_modules/electron/dist — run: npm --prefix \"$PARANEXT_CORE\" install"
# 2. Extension built (and built AFTER paranext-core's install, or its webview 'yjs' import is blank).
[ -d "$EXT_DIST_ABS" ] \
  || fail "Extension not built: $EXT_DIST_ABS — run: npm --prefix \"$REPO_ROOT/platform.bible-extension\" run build"
# 2b. FwLiteWeb backend bundled. Without it the app chrome + webviews still load, but FwLiteWeb
#     fails to spawn (ENOENT) so no lexicon/auth data appears — the auth section renders empty.
ls "$EXT_DIST_ABS"/fw-lite/*/FwLiteWeb* >/dev/null 2>&1 \
  || echo "WARN: no FwLiteWeb under $EXT_DIST_ABS/fw-lite — auth/data won't work (chrome will). Run: task build-fw-lite, then rebuild the extension." >&2
# 3. CDP enabled in paranext-core.
grep -q "remote-debugging-port" "$PARANEXT_CORE/package.json" \
  || fail "CDP not enabled — run: node \"$SKILL_DIR/enable-cdp.js\""

EXT_DIST_REL="$(realpath --relative-to="$PARANEXT_CORE" "$EXT_DIST_ABS")"
# An absolute result means the paths couldn't be relativized (mismatched forms); loading it would
# fail silently, so stop with an actionable message instead.
case "$EXT_DIST_REL" in
  /* | [A-Za-z]:*) fail "Computed --extensions path is absolute ($EXT_DIST_REL). Pass PARANEXT_CORE (and EXT_DIST) in the shell's unix form, e.g. PARANEXT_CORE=/d/code/paranext-core." ;;
esac
echo "paranext-core : $PARANEXT_CORE"
echo "extension     : $EXT_DIST_REL (relative to paranext-core)"

cd "$PARANEXT_CORE"
export MAIN_ARGS="--extensions $EXT_DIST_REL"
exec npm start
