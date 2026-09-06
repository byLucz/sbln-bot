#!/usr/bin/env bash
set -euo pipefail
ROOT=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
channel=${1:-stable}
mode=${2:-}
[[ "$channel" = stable || "$channel" = proto ]] || { echo 'install.sh [stable|proto] [--local]' >&2; exit 1; }
[[ -z "$mode" || "$mode" = --local ]] || { echo 'install.sh [stable|proto] [--local]' >&2; exit 1; }
[[ ${EUID:-$(id -u)} = 0 ]] || { echo 'Запусти через sudo/root' >&2; exit 1; }
install -m 0755 "$ROOT/sbln.sh" /usr/local/bin/sbln
install -m 0755 "$ROOT/sblnproto.sh" /usr/local/bin/sblnproto
if [[ "$mode" = --local ]]; then
  SBLN_INSTANCE="$channel" exec bash /usr/local/bin/sbln deploy "$ROOT"
fi
SBLN_INSTANCE="$channel" exec bash /usr/local/bin/sbln deploy
