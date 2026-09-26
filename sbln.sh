#!/usr/bin/env bash
set -euo pipefail
BASE=${SBLN_BASE:-/opt/sbln}
APP="$BASE/app"
SVC=sbln-service
BOT=sbln-bot
[[ "$BASE" =~ ^/[a-zA-Z0-9_./-]+$ && "$BASE" != / && "$BASE" != *..* ]] || exit 1
if ! docker info >/dev/null 2>&1 && [[ ${EUID:-$(id -u)} -ne 0 ]] && command -v sudo >/dev/null 2>&1; then
  exec sudo -E bash "$0" "$@"
fi
cmd=${1:-menu}
if [[ $# -gt 0 ]]; then shift; fi

wait_ready() {
  local name=${1:-$BOT} state
  for ((attempt=0; attempt<60; attempt++)); do
    state=$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$name" 2>/dev/null || true)
    if [[ "$state" = healthy ]]; then return; fi
    if [[ "$state" = unhealthy || "$state" = exited || "$state" = dead ]]; then break; fi
    sleep 2
  done
  echo "Контейнер $name не готов. Последние логи:" >&2
  docker logs --tail 60 "$name" >&2 || true
  return 1
}

network_mode() {
  local config=$1 network=${SBLN_NET:-}
  if [[ -z "$network" ]]; then
    command -v jq >/dev/null 2>&1 || { echo 'Для чтения Docker.NetworkMode нужен jq: apt install jq' >&2; return 1; }
    network=$(jq -er 'if .Docker.NetworkMode == null then "bridge" else .Docker.NetworkMode end | if . == "host" or . == "bridge" then . else error("Docker.NetworkMode: expected host or bridge") end' "$config") || return 1
  fi
  [[ "$network" = host || "$network" = bridge ]] || { echo 'Сетевой режим должен быть host или bridge' >&2; return 1; }
  printf '%s\n' "$network"
}

docker_root() {
  local dir
  dir=$(docker info --format '{{.DockerRootDir}}' 2>/dev/null | head -1) || dir=''
  [[ -n "$dir" && -d "$dir" ]] || dir=/var/lib/docker
  printf '%s
' "$dir"
}

free_kb() {
  local dir
  dir=$(docker_root)
  [[ -d "$dir" ]] || return 0
  df -P "$dir" 2>/dev/null | awk 'NR==2 {print $4}'
}

prune() {
  local keep=${1:-72h} before after freed
  before=$(free_kb)
  docker image prune -f >/dev/null 2>&1 || true
  docker builder prune -f --filter "until=$keep" >/dev/null 2>&1 || true
  after=$(free_kb)

  if [[ -z "$after" ]]; then
    echo '>> очистка docker выполнена'
    return 0
  fi

  if [[ -n "$before" ]] && (( after > before )); then
    freed=$(( (after - before) / 1024 ))
    echo ">> очистка docker: освобождено ${freed} МБ, свободно $(( after / 1024 / 1024 )) ГБ"
  else
    echo ">> очистка docker: удалять нечего, свободно $(( after / 1024 / 1024 )) ГБ"
  fi
}

check_space() {
  local need_mb=${1:-3072} free
  free=$(free_kb)
  [[ -n "$free" ]] || return 0
  if (( free / 1024 < need_mb )); then
    echo ">> мало места под docker: $(( free / 1024 )) МБ, чищу перед сборкой" >&2
    prune 24h
  fi
}

build() {
  local source=${1:-$APP} channel=${2:-stable} version sha network config="$BASE/config.json" name="$BOT" channel_arg=''
  [[ "$channel" = stable || "$channel" = proto ]] || exit 1
  source=$(cd -- "$source" && pwd)
  if [[ "$channel" = proto ]]; then config="$BASE/config-proto.json"; name=sbln-bot-proto; channel_arg=proto; fi
  [[ -f "$config" ]] || { echo "Нет $config" >&2; exit 1; }
  network=$(network_mode "$config")
  sha=$(git -C "$source" rev-parse HEAD)
  if [[ "$channel" = stable ]]; then
    version=$(git -C "$source" tag --points-at HEAD -l 'v*' | sed -nE 's/^v([0-9]+\.[0-9]+\.[0-9]+)$/\1/p' | sort -V | tail -1)
    [[ -n "$version" ]] || { echo 'На HEAD master ещё нет релизного тега. Дождись CI и повтори.' >&2; exit 1; }
  else
    version=$(sed -nE 's/.*<SblnVersion>([0-9]+\.[0-9]+\.[0-9]+)<\/SblnVersion>.*/\1/p' "$source/Directory.Build.props")
  fi
  [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || exit 1
  mkdir -p "$BASE/data/$channel" "$BASE/logs/$channel" "$BASE/audio/$channel"
  cat > "$source/.env" <<EOF
SBLN_IMAGE=sbln-bot:$channel
SBLN_SOURCE=$source
SBLN_VERSION=$version
SBLN_CHANNEL=$channel_arg
SBLN_COMMIT=$sha
SBLN_CONTAINER=$name
SBLN_INSTANCE=$channel
SBLN_NETWORK_MODE=$network
SBLN_CONFIG_FILE=$config
SBLN_DATA_DIR=$BASE/data/$channel
SBLN_LOG_DIR=$BASE/logs/$channel
SBLN_AUDIO_ROOT=$BASE/audio
EOF
  check_space
  (cd "$source" && docker compose --project-name "sbln-$channel" build)
  prune
}

menu() {
  local choice
  local -a action
  trap 'printf "\n"' INT
  while true; do
    printf '\n── sbln · production ──\n1 Статус\n2 Логи (Ctrl+C — назад)\n3 Запустить\n4 Остановить\n5 Перезапустить\n6 Обновить из master\n7 Статус Lavalink\n8 Перезапустить Lavalink\n0 Выход\n'
    read -r -p 'Выбери пункт: ' choice || break
    case "$choice" in
      1) action=(status) ;; 2) action=(logs) ;; 3) action=(start) ;; 4) action=(stop) ;;
      5) action=(restart) ;; 6) action=(update) ;; 7) action=(lava status) ;; 8) action=(lava restart) ;;
      9) action=(prune) ;;
      0|q) break ;; *) continue ;;
    esac
    if bash "${BASH_SOURCE[0]}" "${action[@]}"; then :; else echo 'Команда не завершилась успешно.' >&2; fi
  done
  trap - INT
}

case "$cmd" in
  menu) menu ;;
  status)
    systemctl --no-pager -n0 status "$SVC" || true
    docker ps --filter "name=^/$BOT$" --format 'бот: {{.Status}}'
    ;;
  logs) docker logs -f --tail "${1:-100}" "$BOT" ;;
  start|restart) systemctl "$cmd" "$SVC"; wait_ready ;;
  stop) systemctl stop "$SVC" ;;
  wait) wait_ready "${1:-$BOT}" ;;
  build) build "${1:-$APP}" "${2:-stable}" ;;
  prune) prune "${1:-72h}" ;;
  network) network_mode "${1:-$BASE/config.json}" ;;
  update)
    git -C "$APP" fetch -q origin --tags
    git -C "$APP" checkout -q master
    git -C "$APP" reset -q --hard origin/master
    git -C "$APP" submodule update --init --recursive
    if command -v jq >/dev/null 2>&1 && [[ -f "$BASE/config.json" ]]; then
      tmp=$(mktemp)
      if jq -s '.[0] * .[1]' "$APP/config/config.example.json" "$BASE/config.json" > "$tmp" 2>/dev/null && [[ -s "$tmp" ]] && ! cmp -s "$tmp" "$BASE/config.json"; then
        cat "$tmp" > "$BASE/config.json"; echo '>> конфиг дополнен новыми ключами из примера'
      fi
      rm -f "$tmp"
    fi
    bash "$APP/sbln.sh" build "$APP"
    install -m 0755 "$APP/sbln.sh" /usr/local/bin/sbln
    [[ -e /usr/local/bin/sblnproto && -f "$APP/sblnproto.sh" ]] && install -m 0755 "$APP/sblnproto.sh" /usr/local/bin/sblnproto
    systemctl restart "$SVC"
    wait_ready
    echo 'Обновлено из master, служба перезапущена.'
    ;;
  lava)
    name=$(docker ps --format '{{.Names}}' | sed -n '/[Ll]avalink/{p;q;}')
    case "${1:-status}" in
      status) if [[ -n "$name" ]]; then docker inspect --format '{{.State.Status}}' "$name"; else systemctl status lavalink --no-pager; fi ;;
      restart) if [[ -n "$name" ]]; then docker restart "$name"; else systemctl restart lavalink; fi ;;
      *) echo 'lava status|restart' >&2; exit 1 ;;
    esac
    ;;
  help) echo 'sbln: меню | status | logs [N] | start | stop | restart | update | prune [until] | lava status|restart' ;;
  *) echo "Неизвестная команда: $cmd" >&2; exit 1 ;;
esac
