#!/usr/bin/env bash
set -uo pipefail
STATE=${RAGENT_STATE:-/var/lib/private/ragent}

avail() { df -B1 --output=avail / | tail -1 | tr -dc 0-9; }

rm -f "$STATE/clean.request"

before=$(avail)
if command -v docker >/dev/null 2>&1; then
  docker image prune -af --filter "until=72h" >/dev/null 2>&1 || true
  docker builder prune -af >/dev/null 2>&1 || true
  find /var/lib/docker/containers -name '*-json.log' -size +50M -exec truncate -s 0 {} + 2>/dev/null || true
fi
journalctl --vacuum-size=200M >/dev/null 2>&1 || true
apt-get clean >/dev/null 2>&1 || true
find /var/log -type f \( -name '*.gz' -o -name '*.[0-9]' -o -name '*.old' \) -mtime +3 -delete 2>/dev/null || true
after=$(avail)

freed=$(( after > before ? after - before : 0 ))
mkdir -p "$STATE"
printf '{"ok": true, "freed": %s, "free": %s, "at": %s}\n' "$freed" "$after" "$(date +%s)" > "$STATE/clean.result.tmp"
chmod 644 "$STATE/clean.result.tmp"
mv "$STATE/clean.result.tmp" "$STATE/clean.result"
echo "освобождено $((freed / 1048576)) MB, свободно $((after / 1073741824)) GB"
