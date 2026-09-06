#!/usr/bin/env bash
set -euo pipefail
ROOT=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
HELPER="$ROOT/sbln.sh"
[[ -f "$HELPER" ]] || HELPER="$ROOT/sbln"
if [[ $# = 0 ]]; then set -- status; fi
SBLN_INSTANCE=proto exec bash "$HELPER" "$@"
