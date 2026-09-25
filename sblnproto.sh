#!/usr/bin/env bash
set -euo pipefail
BASE=${SBLN_BASE:-/opt/sbln}
REPO=${SBLN_REPO:-https://github.com/byLucz/sblngavna.git}
PROTO="$BASE/proto"
PCONFIG="$BASE/config-proto.json"
PBOT=sbln-bot-proto
[[ "$BASE" =~ ^/[a-zA-Z0-9_./-]+$ && "$BASE" != / && "$BASE" != *..* ]] || exit 1
if ! docker info >/dev/null 2>&1 && [[ ${EUID:-$(id -u)} -ne 0 ]] && command -v sudo >/dev/null 2>&1; then
  exec sudo -E bash "$0" "$@"
fi
ROOT=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
HELPER="$ROOT/sbln.sh"
[[ -f "$HELPER" ]] || HELPER="$ROOT/sbln"
cmd=${1:-menu}
if [[ $# -gt 0 ]]; then shift; fi

menu() {
  local choice
  local -a action
  trap 'printf "\n"' INT
  while true; do
    printf '\n── sblnproto ──\n1 Статус proto\n2 Логи proto (Ctrl+C — назад)\n3 Обновить из proto и пересобрать\n4 Удалить контейнер proto\n5 Перезапустить proto\n6 Запустить proto\n7 Остановить proto\n0 Выход\n'
    read -r -p 'Выбери пункт: ' choice || break
    case "$choice" in
      1) action=(status) ;; 2) action=(logs) ;; 3) action=(update) ;; 4) action=(down) ;;
      5) action=(restart) ;; 6) action=(start) ;; 7) action=(stop) ;;
      0|q) break ;; *) continue ;;
    esac
    if bash "${BASH_SOURCE[0]}" "${action[@]}"; then :; else echo 'Команда не завершилась успешно.' >&2; fi
  done
  trap - INT
}

case "$cmd" in
  menu) menu ;;
  hotswap|update)
    [[ -f "$PCONFIG" ]] || { echo "Нет $PCONFIG. Запусти install.sh proto." >&2; exit 1; }
    if [[ -n "${1:-}" ]]; then
      PROTO=$(cd -- "$1" && pwd)
    elif [[ -d "$PROTO/.git" ]]; then
      git -C "$PROTO" fetch -q origin --tags
      git -C "$PROTO" checkout -q proto
      git -C "$PROTO" reset -q --hard origin/proto
    else
      mkdir -p "$BASE"
      git clone --branch proto --recurse-submodules "$REPO" "$PROTO"
    fi
    if [[ -z "${1:-}" ]]; then
      exec bash "$PROTO/sblnproto.sh" hotswap "$PROTO"
    fi
    [[ -d "$PROTO/.git" ]] && git -C "$PROTO" submodule update --init --recursive
    if command -v jq >/dev/null 2>&1 && [[ -f "$PCONFIG" ]]; then
      tmp=$(mktemp)
      if jq -s '.[0] * .[1]' "$PROTO/config/config-proto.example.json" "$PCONFIG" > "$tmp" 2>/dev/null && [[ -s "$tmp" ]] && ! cmp -s "$tmp" "$PCONFIG"; then
        cat "$tmp" > "$PCONFIG"; echo '>> proto-конфиг дополнен новыми ключами из примера'
      fi
      rm -f "$tmp"
    fi
    install -m 0755 "$PROTO/sbln.sh" /usr/local/bin/sbln
    install -m 0755 "$PROTO/sblnproto.sh" /usr/local/bin/sblnproto
    HELPER=/usr/local/bin/sbln
    network=$(bash "$HELPER" network "$PCONFIG")
    echo "Сеть proto: $network (конфиг: $PCONFIG)"
    bash "$HELPER" build "$PROTO" proto
    docker rm -f "$PBOT" >/dev/null 2>&1 || true
    net_args=(--network "$network" --add-host host.docker.internal:host-gateway)
    docker run -d --name "$PBOT" --restart no --init --stop-timeout 30 \
      -e SBLN_CONFIG=/opt/sbln/config.json -e SBLN_READY_FILE=/tmp/sbln-ready \
      -e SBLN_AUDIO_DIR=/opt/sbln/audio/proto -e SBLN_LOG_DIR=/opt/sbln/logs \
      -v "$PCONFIG":/opt/sbln/config.json:ro \
      -v "$BASE/data/proto":/opt/sbln/data -v "$BASE/logs/proto":/opt/sbln/logs \
      -v "$BASE/audio":/opt/sbln/audio -v /var/run/docker.sock:/var/run/docker.sock \
      --log-opt max-size=10m --log-opt max-file=3 \
      "${net_args[@]}" sbln-bot:proto >/dev/null
    bash "$HELPER" wait "$PBOT"
    echo 'Proto запущен. Production-контейнер не перезапускался.'
    ;;
  down) docker rm -f "$PBOT" >/dev/null; echo 'Proto остановлен и удалён.' ;;
  start) docker start "$PBOT"; bash "$HELPER" wait "$PBOT" ;;
  stop) docker stop "$PBOT" ;;
  restart) docker restart "$PBOT"; bash "$HELPER" wait "$PBOT" ;;
  logs) docker logs -f --tail "${1:-100}" "$PBOT" ;;
  status|pstatus) docker ps -a --filter "name=^/$PBOT$" --format 'proto: {{.Status}}' ;;
  help) echo 'Без аргументов — меню proto. Команды: status | logs [N] | start | stop | restart | update [checkout] | hotswap [checkout] | down' ;;
  *) echo "Неизвестная команда proto: $cmd. Используй help." >&2; exit 1 ;;
esac
