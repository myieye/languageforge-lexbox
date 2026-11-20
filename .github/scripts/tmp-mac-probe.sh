#!/usr/bin/env bash
# TEMPORARY fork-CI probe, never merged. Runs the app twice: once writing keychain/file test values, once reading them.
set -uo pipefail
APP="$1"
OUT="$RUNNER_TEMP/probe"; mkdir -p "$OUT"
EXE="$APP/Contents/MacOS/$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP/Contents/Info.plist")"
codesign -d --entitlements - "$APP" 2>&1 | head -40
codesign -dv "$APP" 2>&1 | head
security list-keychains; security default-keychain; security show-keychain-info "$(security default-keychain | tr -d '" ')" 2>&1
for mode in write read logout after-logout; do
  FWLITE_PROBE=$mode "$EXE" > "$OUT/$mode.stdout" 2>&1 &
  pid=$!
  for _ in $(seq 180); do grep -q 'PROBE: DONE' "$OUT/$mode.stdout" && break; kill -0 $pid 2>/dev/null || break; sleep 1; done
  sleep 3
  kill $pid 2>/dev/null; for _ in $(seq 10); do kill -0 $pid 2>/dev/null || break; sleep 1; done; kill -9 $pid 2>/dev/null; wait $pid 2>/dev/null
  echo "== $mode run"; grep 'PROBE' "$OUT/$mode.stdout" || { echo "no probe output"; tail -50 "$OUT/$mode.stdout"; }
done
find "$HOME/Library/Containers/org.sil.FwLiteMaui" -name 'app.log' -not -path '*/tmp/*' -exec tail -30 {} \; 2>/dev/null
grep -q 'PROBE: DONE' "$OUT/after-logout.stdout" || exit 1
# pass only if the fresh process still sees the login written by the first one
grep -q 'PROBE: IsSignedIn(lexbox.org)=True' "$OUT/read.stdout" || { echo "::error::login not kept across restart"; exit 1; }
echo "Login kept across restart"
grep -q 'PROBE: IsSignedIn(lexbox.org)=False' "$OUT/after-logout.stdout" || { echo "::error::logout not saved across restart"; exit 1; }
echo "Logout saved by MSAL and kept across restart"
