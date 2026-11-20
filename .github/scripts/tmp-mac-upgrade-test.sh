#!/usr/bin/env bash
# TEMPORARY fork-CI test, never merged. Installs an old FW Lite DMG, gives it a project, then updates to a new DMG
# the way a user would and checks the project survives.
# Usage: tmp-mac-upgrade-test.sh <old.dmg> <new.dmg> <project.sqlite> <replace|trash>
set -uo pipefail
OLD_DMG="$1"; NEW_DMG="$2"; PROJECT_DB="$3"; MODE="$4"
BUNDLE=org.sil.FwLiteMaui
APP="/Applications/FieldWorks Lite.app"
C="$HOME/Library/Containers/$BUNDLE"
OUT="$RUNNER_TEMP/upgrade"; mkdir -p "$OUT"
CODE=qaa-upgrade
FAILED=0
fail() { echo "::error::$*"; FAILED=1; }

install_dmg() {
  local mnt="/Volumes/fwl-$RANDOM" try=0
  until hdiutil attach "$1" -nobrowse -readonly -mountpoint "$mnt" >/dev/null; do (( ++try < 5 )) || return 1; sleep $((try * 5)); done
  ditto "$mnt/FieldWorks Lite.app" "$APP"
  hdiutil detach "$mnt" >/dev/null || hdiutil detach -force "$mnt" >/dev/null
  echo "Installed version $(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$APP/Contents/Info.plist")"
  codesign -dv "$APP" 2>&1 | grep -E '^(Identifier|TeamIdentifier)='
}

app_log() { find "$C" -name app.log -not -path '*/tmp/*' 2>/dev/null | head -1; }

# Runs the installed app until it logs "Viewer loaded" (only counting log lines written by this launch), then for $2 more seconds.
run_app() {
  local label="$1" extra="$2" exe log before=0 loaded=0
  exe="$APP/Contents/MacOS/$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP/Contents/Info.plist")"
  log="$(app_log)"; [ -n "$log" ] && before=$(wc -l < "$log")
  touch "$OUT/$label.start"
  "$exe" > "$OUT/$label.stdout" 2>&1 &
  local pid=$!
  for _ in $(seq 120); do
    log="$(app_log)"
    if grep -qs 'Viewer loaded' "$OUT/$label.stdout" || { [ -n "$log" ] && tail -n +$((before + 1)) "$log" | grep -qs 'Viewer loaded'; }; then loaded=1; break; fi
    kill -0 "$pid" 2>/dev/null || break
    sleep 1
  done
  [ "$loaded" = 1 ] && sleep "$extra"
  kill "$pid" 2>/dev/null; for _ in $(seq 15); do kill -0 "$pid" 2>/dev/null || break; sleep 1; done; kill -9 "$pid" 2>/dev/null; wait "$pid" 2>/dev/null
  log="$(app_log)"
  [ -n "$log" ] && tail -n +$((before + 1)) "$log" > "$OUT/$label.app.log"
  echo "::group::$label app.log (this launch)"; cat "$OUT/$label.app.log" 2>/dev/null; echo "::endgroup::"
  [ "$loaded" = 1 ] || fail "$label: the app never logged 'Viewer loaded'"
}

snapshot() { # files that hold user data, with checksums; skips caches and logs that change on every launch
  find "$C/Data" -type f -not -path '*/tmp/*' -not -path '*/Caches/*' -not -path '*/WebKit/*' -not -name 'app*.log' -not -name '*.sqlite-shm' -not -name '*.sqlite-wal' -print0 2>/dev/null \
    | sort -z | xargs -0 shasum 2>/dev/null | sed "s|$C||"
}

echo "== Install the old version"
install_dmg "$OLD_DMG" || { fail "could not install old DMG"; exit 1; }
run_app old-first 5
LOG="$(app_log)"
[ -n "$LOG" ] || { fail "no app.log in $C: the old app isn't sandboxed or the container is unreadable"; find "$HOME/Library" -maxdepth 3 -iname '*fwlite*' 2>/dev/null; exit 1; }
BASE="$(dirname "$LOG")"
echo "Data folder: $BASE"
ls -la "$BASE"

echo "== Give the old version a project and open it"
cp "$PROJECT_DB" "$BASE/$CODE.sqlite"
echo "marker $(date)" > "$BASE/upgrade-marker.txt"
PREFS="$C/Data/Library/Preferences/$BUNDLE.plist"
ls -la "$C/Data/Library/Preferences" 2>/dev/null
defaults write "$PREFS" AppLastUrl -string "/project/$CODE"
defaults read "$PREFS" 2>/dev/null
DB_SUM_PLANTED="$(shasum "$BASE/$CODE.sqlite" | cut -d' ' -f1)"
run_app old-open 25
DB_SUM_OLD="$(shasum "$BASE/$CODE.sqlite" | cut -d' ' -f1)"
[ "$DB_SUM_PLANTED" != "$DB_SUM_OLD" ] && echo "The old version wrote to the project (it opened it)" || echo "The old version did not change the project file"
ls -la "$BASE"
snapshot > "$OUT/before-update.txt"

echo "== Update ($MODE)"
case "$MODE" in
  replace) rm -rf "$APP" ;;   # Finder's Replace removes the old bundle, then copies the new one
  trash) mv "$APP" "$HOME/.Trash/" && rm -rf "$HOME/.Trash/FieldWorks Lite.app" ;;
esac
[ -d "$APP" ] && fail "old app still present"
[ -d "$C" ] || fail "the container disappeared when the app was removed"
snapshot > "$OUT/after-remove.txt"
diff "$OUT/before-update.txt" "$OUT/after-remove.txt" && echo "Removing the app changed no data files" || fail "removing the app changed data files"
install_dmg "$NEW_DMG" || { fail "could not install new DMG"; exit 1; }
run_app new 25

echo "== Check the data after the update"
ls -la "$BASE"
snapshot > "$OUT/after-update.txt"
[ -f "$BASE/upgrade-marker.txt" ] || fail "marker file gone"
[ -f "$BASE/$CODE.sqlite" ] || fail "project file gone"
[ -s "$OUT/new.app.log" ] && echo "The new version logged into the same container" || fail "the new version wrote no log lines into the old container"
grep -h -i -E 'version|starting' "$OUT/old-first.app.log" "$OUT/new.app.log" 2>/dev/null | head -5
if find "$BASE" -maxdepth 1 -name "$CODE.sqlite*" -newer "$OUT/new.start" | grep -q .; then echo "The new version opened the project (its files changed after launch)"; else echo "::warning::No sign the new version opened the project"; fi
grep -h -i -E "$CODE|exception|error|fail" "$OUT/new.app.log" "$OUT/new.stdout" 2>/dev/null | head -40
echo "Data file changes from before the update to after the new version ran (expected: only the project db and prefs):"
diff "$OUT/before-update.txt" "$OUT/after-update.txt"
cp -R "$BASE" "$OUT/data-folder" 2>/dev/null
exit $FAILED
