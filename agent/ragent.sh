#!/usr/bin/env bash
set -euo pipefail
token=${1:-}
port=${2:-8687}
branch=${RAGENT_BRANCH:-proto}
RAW=${RAGENT_RAW:-https://raw.githubusercontent.com/byLucz/sblngavna/$branch/agent}
DIR=/opt/ragent
UNIT=/etc/systemd/system/ragent.service
[[ ${EUID:-$(id -u)} = 0 ]] || { echo 'Запусти через sudo/root' >&2; exit 1; }
[[ "$port" =~ ^[0-9]+$ ]] || { echo 'ragent.sh [токен] [порт]' >&2; exit 1; }
command -v python3 >/dev/null || { apt-get update -qq && apt-get install -y -qq python3; }
command -v curl >/dev/null || { apt-get update -qq && apt-get install -y -qq curl; }
if [[ -z "$token" && -f "$DIR/ragent.env" ]]; then
  token=$(sed -n 's/^RAGENT_TOKEN=//p' "$DIR/ragent.env")
fi
[[ -n "$token" ]] || token=$(python3 -c 'import secrets; print(secrets.token_urlsafe(24))')
mkdir -p "$DIR"
curl -fsSL "$RAW/ragent.py" -o "$DIR/ragent.py.tmp"
curl -fsSL "$RAW/domains.txt" -o "$DIR/domains.txt.tmp"
python3 -m py_compile "$DIR/ragent.py.tmp"
mv "$DIR/ragent.py.tmp" "$DIR/ragent.py"
mv "$DIR/domains.txt.tmp" "$DIR/domains.txt"
rm -rf "$DIR/__pycache__"
chmod 755 "$DIR"
chmod 644 "$DIR/ragent.py" "$DIR/domains.txt"
umask 077
cat > "$DIR/ragent.env" <<EOF
RAGENT_TOKEN=$token
RAGENT_PORT=$port
EOF
cat > "$UNIT" <<EOF
[Unit]
Description=R-WATCHER agent
After=network-online.target
Wants=network-online.target

[Service]
EnvironmentFile=$DIR/ragent.env
ExecStart=/usr/bin/env python3 $DIR/ragent.py
Restart=always
RestartSec=5
DynamicUser=yes
NoNewPrivileges=yes
ProtectSystem=strict
ProtectHome=yes
PrivateTmp=yes

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable ragent >/dev/null
systemctl restart ragent
sleep 1
if curl -fsS -H "Authorization: Bearer $token" "http://127.0.0.1:$port/health" >/dev/null; then
  echo ">> R-WATCHER agent запущен на порту $port"
else
  echo ">> агент не ответил, смотри: journalctl -u ragent -n 50" >&2
  exit 1
fi
echo ">> токен: $token"
echo ">> в конфиг бота: Remna:AgentToken = этот токен, Remna:AgentPort = $port"
echo ">> открой порт только для IP бота: ufw allow from <IP бота> to any port $port proto tcp"
