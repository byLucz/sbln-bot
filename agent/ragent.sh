#!/usr/bin/env bash
set -euo pipefail
DIR=/opt/ragent
ENV="$DIR/ragent.env"
UNIT=/etc/systemd/system/ragent.service
BIN=/usr/local/bin/ragent

setting() { [[ -f "$ENV" ]] && sed -n "s/^$1=//p" "$ENV" || true; }
root() { [[ ${EUID:-$(id -u)} = 0 ]] || { echo 'Запусти через sudo/root' >&2; exit 1; }; }
branch=${RAGENT_BRANCH:-$(setting RAGENT_BRANCH)}
branch=${branch:-proto}
RAW=${RAGENT_RAW:-https://raw.githubusercontent.com/byLucz/sblngavna/$branch/agent}

health() {
  local token port path ts sig
  token=$(setting RAGENT_TOKEN); port=$(setting RAGENT_PORT)
  path="/${1:-health}"; ts=$(date +%s)
  sig=$(python3 -c 'import hashlib, hmac, sys; print(hmac.new(sys.argv[1].encode(), sys.argv[2].encode(), hashlib.sha256).hexdigest())' "$token" "GET"$'\n'"$path"$'\n'"$ts")
  curl -fsS -H "X-Ragent-Ts: $ts" -H "X-Ragent-Sig: $sig" "http://127.0.0.1:${port:-8687}$path"
}

install_agent() {
  root
  local token=${1:-} port=${2:-}
  [[ -z "$port" || "$port" =~ ^[0-9]+$ ]] || { echo 'ragent [токен] [порт]' >&2; exit 1; }
  command -v python3 >/dev/null || { apt-get update -qq && apt-get install -y -qq python3; }
  command -v curl >/dev/null || { apt-get update -qq && apt-get install -y -qq curl; }
  token=${token:-$(setting RAGENT_TOKEN)}
  port=${port:-$(setting RAGENT_PORT)}
  port=${port:-8687}
  [[ -n "$token" ]] || token=$(python3 -c 'import secrets; print(secrets.token_urlsafe(24))')
  mkdir -p "$DIR"
  local fresh="?t=$(date +%s)"
  curl -fsSL "$RAW/ragent.py$fresh" -o "$DIR/ragent.py.tmp"
  curl -fsSL "$RAW/domains.txt$fresh" -o "$DIR/domains.txt.tmp"
  curl -fsSL "$RAW/ragent-clean.sh$fresh" -o "$DIR/ragent-clean.sh.tmp"
  curl -fsSL "$RAW/ragent.sh$fresh" -o "$BIN.tmp"
  grep -q 'install_agent' "$BIN.tmp" || { echo '>> скачалась старая версия ragent.sh, повтори через пару минут' >&2; exit 1; }
  python3 -m py_compile "$DIR/ragent.py.tmp"
  bash -n "$BIN.tmp"
  bash -n "$DIR/ragent-clean.sh.tmp"
  mv "$DIR/ragent.py.tmp" "$DIR/ragent.py"
  install -m 0755 "$DIR/ragent-clean.sh.tmp" "$DIR/ragent-clean.sh" && rm -f "$DIR/ragent-clean.sh.tmp"
  mv "$DIR/domains.txt.tmp" "$DIR/domains.txt"
  install -m 0755 "$BIN.tmp" "$BIN" && rm -f "$BIN.tmp"
  rm -rf "$DIR/__pycache__"
  chmod 755 "$DIR"
  chmod 644 "$DIR/ragent.py" "$DIR/domains.txt"
  (umask 077 && printf 'RAGENT_TOKEN=%s\nRAGENT_PORT=%s\nRAGENT_BRANCH=%s\n' "$token" "$port" "$branch" > "$ENV")
  cat > "$UNIT" <<EOF
[Unit]
Description=R-WATCHER agent
After=network-online.target
Wants=network-online.target

[Service]
EnvironmentFile=$ENV
ExecStart=/usr/bin/env python3 $DIR/ragent.py
Restart=always
RestartSec=5
DynamicUser=yes
NoNewPrivileges=yes
ProtectSystem=strict
ProtectHome=yes
PrivateTmp=yes
StateDirectory=ragent

[Install]
WantedBy=multi-user.target
EOF
  cat > /etc/systemd/system/ragent-clean.service <<EOF
[Unit]
Description=R-WATCHER agent disk cleanup

[Service]
Type=oneshot
ExecStart=$DIR/ragent-clean.sh
EOF
  cat > /etc/systemd/system/ragent-clean.path <<EOF
[Unit]
Description=R-WATCHER agent cleanup request

[Path]
PathExists=/var/lib/private/ragent/clean.request
Unit=ragent-clean.service

[Install]
WantedBy=multi-user.target
EOF
  systemctl daemon-reload
  systemctl enable ragent >/dev/null
  systemctl restart ragent
  systemctl enable --now ragent-clean.path >/dev/null 2>&1
  sleep 1
  if health >/dev/null; then
    echo ">> R-WATCHER agent запущен на порту $port"
  else
    echo ">> агент не ответил, смотри: ragent logs" >&2
    exit 1
  fi
  echo ">> токен: $token"
  echo ">> в конфиг бота: Remna:AgentToken = этот токен, Remna:AgentPort = $port"
  echo ">> открой порт только для IP бота: ufw allow from <IP бота> to any port $port proto tcp"
  echo ">> управление: ragent help"
}

usage() {
  cat <<'EOF'
ragent [токен] [порт]   установить или переустановить агента
ragent status           статус сервиса и проверка /health
ragent logs [N]         логи агента
ragent start|stop|restart
ragent update           обновить агента и список доменов, токен и порт сохраняются
ragent token            показать токен и порт
ragent scan [домен]     пробный скан с этой ноды, топ-5
ragent clean            очистить диск (образы Docker, логи, кэш apt)
ragent uninstall        удалить агента
EOF
}

case "${1:-}" in
  help|-h|--help) usage ;;
  status)
    systemctl --no-pager status ragent || true
    echo
    health && echo || echo '>> /health не ответил'
    ;;
  logs) journalctl -u ragent -n "${2:-100}" -f ;;
  start|stop|restart) root; systemctl "$1" ragent ;;
  update) install_agent ;;
  token) root; echo "токен: $(setting RAGENT_TOKEN)"; echo "порт: $(setting RAGENT_PORT)" ;;
  clean)
    root
    systemctl start ragent-clean.service
    df -h / | tail -1
    ;;
  scan)
    root
    query="top=5"
    [[ -n "${2:-}" ]] && query="$query&current=$2"
    health "scan?$query" | python3 -m json.tool --no-ensure-ascii
    ;;
  uninstall)
    root
    systemctl disable --now ragent ragent-clean.path 2>/dev/null || true
    rm -f "$UNIT" /etc/systemd/system/ragent-clean.service /etc/systemd/system/ragent-clean.path
    systemctl daemon-reload
    rm -rf "$DIR"
    rm -f "$BIN"
    echo '>> R-WATCHER agent удалён'
    ;;
  *) install_agent "${1:-}" "${2:-}" ;;
esac
