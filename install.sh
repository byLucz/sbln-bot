#!/usr/bin/env bash
set -euo pipefail
channel=${1:-stable}
mode=${2:-}
[[ "$channel" = stable || "$channel" = proto ]] || { echo 'install.sh [stable|proto] [--local]' >&2; exit 1; }
[[ -z "$mode" || "$mode" = --local ]] || exit 1
[[ ${EUID:-$(id -u)} = 0 ]] || { echo 'Запусти через sudo/root' >&2; exit 1; }
BASE=${SBLN_BASE:-/opt/sbln}
REPO=${SBLN_REPO:-https://github.com/byLucz/sblngavna.git}
[[ "$BASE" =~ ^/[a-zA-Z0-9_./-]+$ && "$BASE" != / && "$BASE" != *..* ]] || exit 1
for tool in docker git; do
  command -v "$tool" >/dev/null || { echo "Нужен $tool" >&2; exit 1; }
done
docker info >/dev/null
docker compose version >/dev/null
mkdir -p "$BASE"
if [[ "$mode" = --local ]]; then
  [[ "$channel" = proto ]] || { echo '--local предназначен для стенда proto' >&2; exit 1; }
  [[ -n "${BASH_SOURCE[0]:-}" && -f "${BASH_SOURCE[0]}" ]] || { echo '--local запускается из checkout' >&2; exit 1; }
  ROOT=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
else
  branch=master
  ROOT="$BASE/app"
  if [[ "$channel" = proto ]]; then branch=proto; ROOT="$BASE/proto"; fi
  if [[ -d "$ROOT/.git" ]]; then
    git -C "$ROOT" fetch -q origin --tags
    git -C "$ROOT" checkout -q "$branch"
    git -C "$ROOT" reset -q --hard "origin/$branch"
  else
    git clone --branch "$branch" "$REPO" "$ROOT"
  fi
fi
install -d -m 0755 /usr/local/bin
install -m 0755 "$ROOT/sbln.sh" /usr/local/bin/sbln
install -m 0755 "$ROOT/sblnproto.sh" /usr/local/bin/sblnproto
CONFIG="$BASE/config.json"
example=config.example.json
if [[ "$channel" = proto ]]; then CONFIG="$BASE/config-proto.json"; example=config-proto.example.json; fi
if [[ ! -f "$CONFIG" ]]; then
  install -m 0600 "$ROOT/config/$example" "$CONFIG"
  echo "Создан $CONFIG. Заполни конфиг и повтори установку."
  exit 1
fi
if [[ "$channel" = proto ]]; then
  exec bash /usr/local/bin/sblnproto hotswap "$ROOT"
fi
command -v systemctl >/dev/null || { echo 'Нужен systemd для sbln-service' >&2; exit 1; }
bash /usr/local/bin/sbln build "$ROOT"
docker_bin=$(command -v docker)
cat > /etc/systemd/system/sbln-service.service <<EOF
[Unit]
Description=sbln bot
After=docker.service network-online.target
Requires=docker.service

[Service]
Type=simple
WorkingDirectory=$ROOT
ExecStart=$docker_bin compose --project-name sbln-stable up --no-build
ExecStop=$docker_bin compose --project-name sbln-stable down
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable sbln-service
systemctl restart sbln-service
bash /usr/local/bin/sbln wait
echo 'Готово. Меню: sbln'
