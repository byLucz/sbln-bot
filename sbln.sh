#!/usr/bin/env bash
set -euo pipefail
BASE=${SBLN_BASE:-/opt/sbln}
APP="$BASE/app"
SVC=sbln-service
BOT=sbln-bot
[[ "$BASE" =~ ^/[a-zA-Z0-9_./-]+$ && "$BASE" != / && "$BASE" != *..* ]] || exit 1
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

build() {
  local source=${1:-$APP} channel=${2:-stable} version sha config="$BASE/config.json" name="$BOT" channel_arg=''
  [[ "$channel" = stable || "$channel" = proto ]] || exit 1
  source=$(cd -- "$source" && pwd)
  if [[ "$channel" = proto ]]; then config="$BASE/config-proto.json"; name=sbln-bot-proto; channel_arg=proto; fi
  [[ -f "$config" ]] || { echo "Нет $config" >&2; exit 1; }
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
SBLN_CONFIG_FILE=$config
SBLN_DATA_DIR=$BASE/data/$channel
SBLN_LOG_DIR=$BASE/logs/$channel
SBLN_AUDIO_ROOT=$BASE/audio
EOF
  (cd "$source" && docker compose --project-name "sbln-$channel" build)
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
  update)
    git -C "$APP" fetch -q origin --tags
    git -C "$APP" checkout -q master
    git -C "$APP" reset -q --hard origin/master
    build "$APP"
    install -m 0755 "$APP/sbln.sh" /usr/local/bin/sbln
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
  help) echo 'sbln: меню | status | logs [N] | start | stop | restart | update | lava status|restart' ;;
  *) echo "Неизвестная команда: $cmd" >&2; exit 1 ;;
esac
