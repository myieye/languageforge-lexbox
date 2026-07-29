#!/usr/bin/env bash
# Unpacks the committed slow-sync demo project archive into deployment/_downloads/slow-sync-demo,
# ready for: dotnet run --project backend/FwLite/LcmDebugger -- sync slow-sync-demo
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
downloads="$repo_root/deployment/_downloads"
artifact_dir="$repo_root/backend/FwLite/LcmDebugger/demo"
name="slow-sync-demo"

if [[ -d "$downloads/$name" && -f "$downloads/$name/crdt.sqlite" ]]; then
  echo "$downloads/$name already exists; delete it first to re-unpack." >&2
  exit 1
fi

mkdir -p "$downloads"
if [[ -f "$artifact_dir/$name.tar.zst" ]]; then
  zstd -dc "$artifact_dir/$name.tar.zst" | tar -C "$downloads" -xf -
elif compgen -G "$artifact_dir/$name.tar.zst.part-*" >/dev/null; then
  cat "$artifact_dir/$name.tar.zst".part-* | zstd -dc | tar -C "$downloads" -xf -
else
  echo "No $name.tar.zst(.part-*) found under $artifact_dir — is this the demo branch?" >&2
  exit 1
fi

echo "Unpacked to $downloads/$name"
