#!/usr/bin/env bash
set -euo pipefail
DIR=/opt/ragent
ENV="$DIR/ragent.env"
UNIT=/etc/systemd/system/ragent.service
YT_UNIT=/etc/systemd/system/ragent-yt.service
BIN=/usr/local/bin/ragent

setting() { [[ -f "$ENV" ]] && sed -n "s/^$1=//p" "$ENV" || true; }
root() { [[ ${EUID:-$(id -u)} = 0 ]] || { echo 'Запусти через sudo/root' >&2; exit 1; }; }
branch=${RAGENT_BRANCH:-$(setting RAGENT_BRANCH)}
branch=${branch:-proto}
RAW=${RAGENT_RAW:-https://raw.githubusercontent.com/byLucz/sblngavna/$branch/agent}

pin_raw() {
  [[ -z "${RAGENT_RAW:-}" ]] || return 0
  [[ ! "$RAW" =~ /[0-9a-f]{40}/agent$ ]] || return 0
  local sha
  sha=$(curl -fsSL -m 10 -H 'Accept: application/vnd.github.sha' "https://api.github.com/repos/byLucz/sblngavna/commits/$branch" 2>/dev/null || true)
  if [[ "$sha" =~ ^[0-9a-f]{40}$ ]]; then
    RAW="https://raw.githubusercontent.com/byLucz/sblngavna/$sha/agent"
    echo ">> версия из $branch, коммит ${sha:0:7}"
  else
    echo ">> GitHub API не ответил, качаю по ветке $branch, свежий пуш может прийти через пару минут" >&2
  fi
}

health() {
  local token port path ts sig
  token=$(setting RAGENT_TOKEN); port=${2:-$(setting RAGENT_PORT)}
  path="/${1:-health}"; ts=$(date +%s)
  sig=$(python3 -c 'import hashlib, hmac, sys; print(hmac.new(sys.argv[1].encode(), sys.argv[2].encode(), hashlib.sha256).hexdigest())' "$token" "GET"$'\n'"$path"$'\n'"$ts")
  curl -fsS -H "X-Ragent-Ts: $ts" -H "X-Ragent-Sig: $sig" "http://127.0.0.1:${port:-8687}$path"
}

units() { echo ragent; [[ ! -f "$YT_UNIT" ]] || echo ragent-yt; }

yt_port() { local port; port=$(setting RAGENT_YT_PORT); echo "${port:-8688}"; }

music_health() {
  health health "$(yt_port)" 2>/dev/null | python3 -c 'import json, sys; print(json.load(sys.stdin).get(sys.argv[1]) or "")' "$1" 2>/dev/null || true
}

fetch_music_script() {
  curl -fsSL "$RAW/ragent_yt.py?t=$(date +%s)" -o "$DIR/ragent_yt.py.tmp"
  python3 -m py_compile "$DIR/ragent_yt.py.tmp"
  mv "$DIR/ragent_yt.py.tmp" "$DIR/ragent_yt.py"
  chmod 644 "$DIR/ragent_yt.py"
  rm -rf "$DIR/__pycache__"
}

fetch_music_binaries() {
  local ytdlp deno
  case "$(uname -m)" in
    x86_64|amd64) ytdlp=yt-dlp_linux; deno=deno-x86_64-unknown-linux-gnu.zip ;;
    aarch64|arm64) ytdlp=yt-dlp_linux_aarch64; deno=deno-aarch64-unknown-linux-gnu.zip ;;
    *) echo ">> архитектура $(uname -m) не поддерживается" >&2; exit 1 ;;
  esac
  mkdir -p "$DIR/bin"
  echo '>> качаю yt-dlp и Deno'
  curl -fsSL "https://github.com/yt-dlp/yt-dlp-nightly-builds/releases/latest/download/$ytdlp" -o "$DIR/bin/yt-dlp.tmp"
  curl -fsSL "https://github.com/denoland/deno/releases/latest/download/$deno" -o "$DIR/bin/deno.zip"
  python3 -c 'import sys, zipfile; zipfile.ZipFile(sys.argv[1]).extract("deno", sys.argv[2])' "$DIR/bin/deno.zip" "$DIR/bin"
  rm -f "$DIR/bin/deno.zip"
  install -m 0755 "$DIR/bin/yt-dlp.tmp" "$DIR/bin/yt-dlp" && rm -f "$DIR/bin/yt-dlp.tmp"
  chmod 0755 "$DIR/bin/deno"
  "$DIR/bin/yt-dlp" --version >/dev/null
}

start_music() {
  local port=$1
  if grep -q '^RAGENT_YT_PORT=' "$ENV"; then sed -i "s/^RAGENT_YT_PORT=.*/RAGENT_YT_PORT=$port/" "$ENV"; else echo "RAGENT_YT_PORT=$port" >> "$ENV"; fi
  write_music_unit
  systemctl enable ragent-yt >/dev/null
  systemctl restart ragent-yt
  sleep 1
  health health "$port" >/dev/null || { echo '>> музыка не ответила, смотри: ragent logs' >&2; exit 1; }
}

install_music() {
  root
  local port=${1:-$(yt_port)}
  [[ "$port" =~ ^[0-9]+$ ]] || { echo 'ragent music on [порт]' >&2; exit 1; }
  [[ -f "$ENV" ]] || { echo '>> сначала установи агента: ragent [токен] [порт]' >&2; exit 1; }
  pin_raw
  fetch_music_script
  fetch_music_binaries
  start_music "$port"
  echo ">> музыка включена на порту $port, настройка бота: ragent info"
}

music_update() {
  root
  [[ -f "$YT_UNIT" ]] || { echo '>> музыка не включена: ragent music on' >&2; exit 1; }
  pin_raw
  fetch_music_script
  fetch_music_binaries
  start_music "$(yt_port)"
  sleep 2
  echo ">> yt-dlp обновлён до $("$DIR/bin/yt-dlp" --version)"
}

info() {
  root
  local version port ytport token
  version=$(sed -n 's/^VERSION = "\(.*\)"/\1/p' "$DIR/ragent.py" 2>/dev/null)
  port=$(setting RAGENT_PORT); port=${port:-8687}
  ytport=$(yt_port)
  token=$(setting RAGENT_TOKEN)
  echo "R-WATCHER agent v${version:-?}, ветка $branch"
  echo "агент:   порт $port, служба $(systemctl is-active ragent 2>/dev/null || true)"
  if [[ -f "$YT_UNIT" ]]; then
    echo "музыка:  порт $ytport, служба $(systemctl is-active ragent-yt 2>/dev/null || true), yt-dlp $(music_health ytdlp), cookies $([[ -f "$DIR/cookies.txt" ]] && echo есть || echo нет)"
  else
    echo 'музыка:  выкл (ragent music on)'
  fi
  echo
  echo "токен:   $token"
  echo
  echo 'конфиг бота:'
  echo "  Remna:AgentToken = $token"
  echo "  Remna:AgentPort  = $port"
  if [[ -f "$YT_UNIT" ]]; then
    echo '  Lava:YtAgent     = адрес этой ноды'
    echo "  Lava:YtAgentPort = $ytport"
    echo
    echo 'Lavalink: lavalink.server.sources.http: true'
  fi
  echo
  echo 'ufw, только для IP бота:'
  echo "  ufw allow from <IP бота> to any port $port proto tcp"
  [[ -f "$YT_UNIT" ]] && echo "  ufw allow from <IP бота> to any port $ytport proto tcp"
  return 0
}

write_music_unit() {
  local credential=''
  [[ -f "$DIR/cookies.txt" ]] && credential="LoadCredential=cookies:$DIR/cookies.txt"
  cat > "$YT_UNIT" <<UNIT_END
[Unit]
Description=R-WATCHER agent music
After=network-online.target
Wants=network-online.target

[Service]
EnvironmentFile=$ENV
Environment=PATH=$DIR/bin:/usr/local/bin:/usr/bin:/bin
Environment=DENO_DIR=/var/cache/ragent-yt/deno
Environment=XDG_CACHE_HOME=/var/cache/ragent-yt/xdg
ExecStart=/usr/bin/env python3 $DIR/ragent_yt.py
Restart=always
RestartSec=5
DynamicUser=yes
NoNewPrivileges=yes
ProtectSystem=strict
ProtectHome=yes
PrivateTmp=yes
CacheDirectory=ragent-yt
MemoryMax=400M
CPUQuota=50%
TasksMax=64
$credential

[Install]
WantedBy=multi-user.target
UNIT_END
  systemctl daemon-reload
}

music_cookies() {
  root
  [[ -f "$YT_UNIT" ]] || { echo '>> сначала включи музыку: ragent music on' >&2; exit 1; }
  local source=${1:-}
  if [[ "$source" = off ]]; then
    rm -f "$DIR/cookies.txt"
    echo '>> cookies удалены'
  else
    [[ -f "$source" ]] || { echo 'ragent music cookies <файл cookies.txt в формате Netscape> | off' >&2; exit 1; }
    grep -q $'\t' "$source" || { echo '>> файл не похож на cookies.txt в формате Netscape' >&2; exit 1; }
    install -m 0600 -o root -g root "$source" "$DIR/cookies.txt"
    echo '>> cookies установлены, исходный файл можно удалить'
  fi
  write_music_unit
  systemctl restart ragent-yt
  sleep 1
  health health "$(yt_port)" | python3 -c 'import json, sys; print(">> cookies подхвачены" if json.load(sys.stdin).get("cookies") else ">> музыка работает без cookies")' \
    || echo '>> музыка не ответила, смотри: ragent logs' >&2
}

install_agent() {
  root
  pin_raw
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
  local ytport
  ytport=$(yt_port)
  (umask 077 && printf 'RAGENT_TOKEN=%s\nRAGENT_PORT=%s\nRAGENT_BRANCH=%s\n' "$token" "$port" "$branch" > "$ENV")
  [[ ! -f "$YT_UNIT" ]] || echo "RAGENT_YT_PORT=$ytport" >> "$ENV"
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
  health >/dev/null || { echo '>> агент не ответил, смотри: ragent logs' >&2; exit 1; }
  echo ">> R-WATCHER agent v$(sed -n 's/^VERSION = "\(.*\)"/\1/p' "$DIR/ragent.py") запущен на порту $port"
  if [[ -f "$YT_UNIT" ]]; then
    fetch_music_script
    start_music "$ytport"
  fi
  echo '>> токен и настройка бота: ragent info'
}

usage() {
  cat <<'EOF'
ragent [токен] [порт]   установить или переустановить агента
ragent status           статус служб и проверка /health, вместе с музыкой
ragent logs [N]         логи агента, вместе с музыкой
ragent start|stop|restart
ragent update           обновить агента и список доменов, токен и порт сохраняются
ragent info             версия, токен, порты и что прописать в бота, Lavalink и ufw
ragent scan [домен]     пробный скан с этой ноды, топ-5
ragent clean            очистить диск (образы Docker, логи, кэш apt)
ragent music on [порт]  включить музыку (yt-dlp), порт по умолчанию 8688
ragent music update     обновить yt-dlp и Deno
ragent music cookies <файл>|off   cookies YouTube-аккаунта для 18+ роликов
ragent music off        выключить музыку
ragent uninstall        удалить агента
EOF
}

case "${1:-}" in
  help|-h|--help) usage ;;
  status)
    systemctl --no-pager status $(units) || true
    echo
    health && echo || echo '>> /health не ответил'
    if [[ -f "$YT_UNIT" ]]; then
      health health "$(yt_port)" && echo || echo '>> /health музыки не ответил'
    fi
    ;;
  logs) journalctl $(units | sed 's/^/-u /') -n "${2:-100}" -f ;;
  start|stop|restart) root; systemctl "$1" $(units) ;;
  update) install_agent ;;
  info|token) info ;;
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
  music)
    case "${2:-}" in
      on) install_music "${3:-}" ;;
      update) music_update ;;
      cookies) music_cookies "${3:-}" ;;
      off)
        root
        systemctl disable --now ragent-yt 2>/dev/null || true
        rm -f "$YT_UNIT" "$DIR/ragent_yt.py" "$DIR/cookies.txt"
        rm -rf "$DIR/bin"
        sed -i '/^RAGENT_YT_PORT=/d' "$ENV"
        systemctl daemon-reload
        echo '>> музыка выключена'
        ;;
      *) echo 'ragent music on [порт] | update | off | cookies <файл>|off' >&2; exit 1 ;;
    esac
    ;;
  uninstall)
    root
    systemctl disable --now ragent ragent-yt ragent-clean.path 2>/dev/null || true
    rm -f "$UNIT" "$YT_UNIT" /etc/systemd/system/ragent-clean.service /etc/systemd/system/ragent-clean.path
    systemctl daemon-reload
    rm -rf "$DIR"
    rm -f "$BIN"
    echo '>> R-WATCHER agent удалён'
    ;;
  *) install_agent "${1:-}" "${2:-}" ;;
esac
