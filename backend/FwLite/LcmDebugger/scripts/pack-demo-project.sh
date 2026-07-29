#!/usr/bin/env bash
# Packs a generated slow-sync demo project into deployment/_downloads/slow-sync-demo.tar.zst
# (split into <95MB chunks if needed, since GitHub blocks files >100MB).
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
downloads="$repo_root/deployment/_downloads"
project_dir="${1:-$downloads/slow-sync-demo}"
name="$(basename "$project_dir")"

if [[ ! -f "$project_dir/crdt.sqlite" ]]; then
  echo "No crdt.sqlite in $project_dir — generate the project first (LcmDebugger generate)" >&2
  exit 1
fi

# Fold the WAL into the main db so the archive has a single clean sqlite file.
if command -v sqlite3 >/dev/null && [[ -f "$project_dir/crdt.sqlite-wal" ]]; then
  sqlite3 "$project_dir/crdt.sqlite" "PRAGMA wal_checkpoint(TRUNCATE);" >/dev/null
fi

archive="$downloads/$name.tar.zst"
rm -f "$archive" "$archive".part-*
tar -C "$downloads" --exclude="$name/crdt.sqlite-shm" -cf - "$name" | zstd -12 -T0 -q -o "$archive"

size=$(stat -c%s "$archive")
echo "$archive: $((size / 1024 / 1024))MB"
if (( size > 95 * 1024 * 1024 )); then
  split -b 90M -d "$archive" "$archive.part-"
  rm "$archive"
  ls -la "$archive".part-*
  echo "Archive was >95MB; commit the .part-* files instead."
fi
