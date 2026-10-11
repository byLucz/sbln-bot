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

SYNC_DEF='def sync($e; $u): if ($e | type) == "object" and ($u | type) == "object" and ($e | length) > 0 then reduce ($e | keys_unsorted[]) as $k ({}; .[$k] = (if ($u | has($k)) then sync($e[$k]; $u[$k]) else $e[$k] end)) else $u end;'

config_sync() {
  local example=$1 config=$2 tmp removed
  command -v jq >/dev/null 2>&1 || { echo '>> jq не найден, синхронизация конфига пропущена (apt install jq)' >&2; return 0; }
  [[ -f "$example" && -f "$config" ]] || return 0
  tmp=$(mktemp)
  if ! jq -s "$SYNC_DEF sync(.[0]; .[1])" "$example" "$config" > "$tmp" 2>/dev/null || [[ ! -s "$tmp" ]]; then
    rm -f "$tmp"
    echo ">> не смог разобрать $config или $example, синхронизация пропущена" >&2
    return 0
  fi
  if cmp -s "$tmp" "$config"; then
    rm -f "$tmp"
    return 0
  fi
  removed=$(jq -rs "$SYNC_DEF"' sync(.[0]; .[1]) as $r | .[1] as $u | [($u | [paths]) - ($r | [paths]) | .[]] as $gone | [$gone[] | select(. as $p | any($gone[]; . == $p[:-1]) | not) | map(tostring) | join(".")] | join(", ")' "$example" "$config" 2>/dev/null) || removed=''
  cp -p "$config" "$config.bak"
  cat "$tmp" > "$config"
  rm -f "$tmp"
  echo ">> конфиг $config синхронизирован с примером, прошлая версия: $config.bak"
  [[ -z "$removed" ]] || echo ">> убраны устаревшие ключи: $removed"
  return 0
}

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
  printf '%s\n' "$dir"
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
  if [[ "$keep" = all ]]; then
    docker builder prune -af >/dev/null 2>&1 || true
  else
    docker builder prune -f --filter "until=$keep" >/dev/null 2>&1 || true
  fi
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
    free=$(free_kb)
    if [[ -n "$free" ]] && (( free / 1024 < need_mb )); then
      echo '>> всё ещё мало места, сбрасываю весь кеш сборки' >&2
      prune all
    fi
  fi
}

L_BOT='бот       '
L_SVC='служба    '
L_LAVA='lavalink  '
L_API='lava api  '
L_LINK='связь     '
L_DISK='диск      '

row() {
  local label=$1
  shift
  printf '%s%s\n' "$label" "$*"
}

human_span() {
  local total=${1:-0} days hours mins
  days=$(( total / 86400 ))
  hours=$(( (total % 86400) / 3600 ))
  mins=$(( (total % 3600) / 60 ))
  if (( days > 0 )); then printf '%dд %02d:%02d\n' "$days" "$hours" "$mins"
  else printf '%02d:%02d\n' "$hours" "$mins"; fi
}

container_state() {
  docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$1" 2>/dev/null || true
}

container_span() {
  local started epoch
  started=$(docker inspect --format '{{.State.StartedAt}}' "$1" 2>/dev/null) || return 0
  [[ -n "$started" && "$started" != 0001-01-01T* ]] || return 0
  epoch=$(date -d "$started" +%s 2>/dev/null) || return 0
  printf '%s\n' $(( $(date +%s) - epoch ))
}

lava_name() {
  docker ps --format '{{.Names}} {{.Image}}' 2>/dev/null \
    | awk 'tolower($0) ~ /lavalink|lava/ {print $1; exit}'
}

lava_port() {
  local port=''
  if [[ -f "$1" ]] && command -v jq >/dev/null 2>&1; then
    port=$(jq -r '.Lava.LavaPort // empty' "$1" 2>/dev/null) || port=''
  fi
  [[ "$port" =~ ^[0-9]+$ ]] || port=2333
  printf '%s\n' "$port"
}

lava_pid() {
  local port
  command -v ss >/dev/null 2>&1 || return 1
  port=$(lava_port "$1")
  ss -Hltnp "sport = :$port" 2>/dev/null | head -1 | sed -nE 's/.*pid=([0-9]+).*/\1/p'
}

lava_unit() {
  local pid=$1 unit
  [[ -n "$pid" && -r "/proc/$pid/cgroup" ]] || return 1
  unit=$(sed -nE 's#.*/([^/]+\.service)$#\1#p' "/proc/$pid/cgroup" | head -1)
  [[ -n "$unit" ]] || return 1
  printf '%s\n' "$unit"
}

lava_logfile() {
  local pid=$1 cwd
  [[ -n "$pid" && -r "/proc/$pid/cwd" ]] || return 1
  cwd=$(readlink -f "/proc/$pid/cwd" 2>/dev/null) || return 1
  [[ -d "$cwd/logs" ]] || return 1
  ls -1t "$cwd"/logs/*.log 2>/dev/null | head -1
}

lava_listener() {
  local port line
  command -v ss >/dev/null 2>&1 || return 1
  port=$(lava_port "$1")
  line=$(ss -Hltnp "sport = :$port" 2>/dev/null | head -1)
  [[ -n "$line" ]] || return 1
  printf '%s\n' "$line" | sed -nE 's/.*users:\(\("([^"]+)",pid=([0-9]+).*/\1 (pid \2)/p'
}

lava_stats() {
  local config=$1 host port pass
  command -v jq >/dev/null 2>&1 && command -v curl >/dev/null 2>&1 || return 2
  [[ -f "$config" ]] || return 2
  host=$(jq -r '.Lava.LavaHost // "127.0.0.1"' "$config")
  port=$(jq -r '.Lava.LavaPort // "2333"' "$config")
  pass=$(jq -r '.Lava.LavaPass // ""' "$config")
  [[ "$host" = host.docker.internal ]] && host=127.0.0.1
  curl -fsS -m 3 -H "Authorization: $pass" "http://$host:$port/v4/stats" 2>/dev/null \
    | jq -r '[((.uptime // 0) / 1000 | floor), (.players // 0), (.playingPlayers // 0), ((.cpu.lavalinkLoad // 0) * 100 | floor), ((.memory.used // 0) / 1048576 | floor), ((.memory.allocated // 0) / 1048576 | floor)] | @tsv'
}

lava_links() {
  local port
  command -v ss >/dev/null 2>&1 || return 1
  port=$(lava_port "$1")
  ss -Htn state established "( sport = :$port )" 2>/dev/null | wc -l
}

panel() {
  local channel=${1:-stable} name="$BOT" config="$BASE/config.json"
  local state span image restarts lava info rc links free listener up players playing load used alloc
  if [[ "$channel" = proto ]]; then name=sbln-bot-proto; config="$BASE/config-proto.json"; fi

  printf '\n── состояние / %s ──\n' "$channel"

  state=$(container_state "$name")
  if [[ -z "$state" ]]; then
    row "$L_BOT" 'контейнера нет'
  else
    span=$(container_span "$name")
    image=$(docker inspect --format '{{.Config.Image}}' "$name" 2>/dev/null || true)
    restarts=$(docker inspect --format '{{.RestartCount}}' "$name" 2>/dev/null || echo 0)
    row "$L_BOT" "$state${span:+, аптайм $(human_span "$span")}${image:+, образ $image}$( (( restarts > 0 )) && printf ', перезапусков %s' "$restarts" )"
  fi

  if [[ "$channel" = stable ]]; then
    row "$L_SVC" "$(systemctl is-active "$SVC" 2>/dev/null || echo 'нет данных')"
  fi

  lava=$(lava_name)
  if [[ -n "$lava" ]]; then
    span=$(container_span "$lava")
    row "$L_LAVA" "$lava: $(container_state "$lava")${span:+, аптайм $(human_span "$span")}"
  elif systemctl is-active lavalink >/dev/null 2>&1; then
    row "$L_LAVA" 'служба active'
  elif listener=$(lava_listener "$config") && [[ -n "$listener" ]]; then
    row "$L_LAVA" "вне docker: $listener"
  else
    row "$L_LAVA" 'не найден'
  fi

  info=$(lava_stats "$config") && rc=0 || rc=$?
  if (( rc == 0 )) && [[ -n "$info" ]]; then
    IFS=$'\t' read -r up players playing load used alloc <<< "$info"
    row "$L_API" "аптайм $(human_span "${up:-0}"), плееров ${playing:-0}/${players:-0}, cpu ${load:-0}%, память ${used:-0}/${alloc:-0} МБ"
  elif (( rc == 2 )); then
    row "$L_API" 'нет jq или curl'
  else
    row "$L_API" 'не отвечает'
  fi

  links=$(lava_links "$config") || links=''
  [[ -n "$links" ]] && row "$L_LINK" "$( (( links > 0 )) && printf 'сессий с ботом: %s' "$links" || printf 'подключений нет' )"

  free=$(free_kb)
  [[ -n "$free" ]] && row "$L_DISK" "свободно $(( free / 1024 / 1024 )) ГБ"

  return 0
}

build() {
  local source=${1:-$APP} channel=${2:-stable} version sha network config="$BASE/config.json" name="$BOT" channel_arg='' restart=unless-stopped
  [[ "$channel" = stable || "$channel" = proto ]] || exit 1
  source=$(cd -- "$source" && pwd)
  if [[ "$channel" = proto ]]; then config="$BASE/config-proto.json"; name=sbln-bot-proto; channel_arg=proto; restart=no; fi
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
SBLN_RESTART=$restart
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
    panel stable || true
    printf '\n1 Статус\n2 Логи (Ctrl+C - назад)\n3 Запустить\n4 Остановить\n5 Перезапустить\n6 Обновить из master\n7 Логи Lavalink\n8 Перезапустить Lavalink\n9 Очистить docker\n0 Выход\n'
    read -r -p 'Выбери пункт: ' choice || break
    case "$choice" in
      1) action=(status) ;; 2) action=(logs) ;; 3) action=(start) ;; 4) action=(stop) ;;
      5) action=(restart) ;; 6) action=(update) ;; 7) action=(lava logs) ;; 8) action=(lava restart) ;;
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
  config-sync) config_sync "${1:?пример}" "${2:?конфиг}" ;;
  update)
    git -C "$APP" fetch -q origin --tags
    git -C "$APP" checkout -q master
    git -C "$APP" reset -q --hard origin/master
    git -C "$APP" submodule update --init --recursive
    bash "$APP/sbln.sh" config-sync "$APP/config/config.example.json" "$BASE/config.json"
    bash "$APP/sbln.sh" build "$APP"
    install -m 0755 "$APP/sbln.sh" /usr/local/bin/sbln
    [[ -e /usr/local/bin/sblnproto && -f "$APP/sblnproto.sh" ]] && install -m 0755 "$APP/sblnproto.sh" /usr/local/bin/sblnproto
    systemctl restart "$SVC"
    wait_ready
    echo 'Обновлено из master, служба перезапущена.'
    ;;
  panel) panel "${1:-stable}" ;;
  lava)
    name=$(lava_name)
    lconfig="$BASE/config.json"
    [[ -f "$lconfig" ]] || lconfig="$BASE/config-proto.json"
    lpid=$(lava_pid "$lconfig") || lpid=''
    lunit=$(lava_unit "$lpid") || lunit=''
    case "${1:-status}" in
      status)
        if [[ -n "$name" ]]; then docker inspect --format '{{.State.Status}}' "$name"
        elif [[ -n "$lunit" ]]; then systemctl status "$lunit" --no-pager
        elif [[ -n "$lpid" ]]; then echo "вне docker и systemd: pid $lpid"
        else echo 'Lavalink не найден' >&2; exit 1; fi
        ;;
      restart)
        if [[ -n "$name" ]]; then
          docker restart "$name" >/dev/null; echo ">> $name перезапущен"
        elif [[ -n "$lunit" ]]; then
          systemctl restart "$lunit"; echo ">> $lunit перезапущен"
        elif [[ -n "$lpid" ]]; then
          echo "Lavalink запущен вручную (pid $lpid), не под systemd и не в docker." >&2
          echo "Перезапуск вслепую опасен: процесс не поднимется сам. Останови и запусти его тем же способом, которым запускал." >&2
          exit 1
        else
          echo 'Lavalink не найден' >&2; exit 1
        fi
        ;;
      logs)
        if [[ -n "$name" ]]; then docker logs -f --tail "${2:-100}" "$name"
        elif [[ -n "$lunit" ]]; then journalctl -u "$lunit" -n "${2:-100}" -f
        elif logfile=$(lava_logfile "$lpid") && [[ -n "$logfile" ]]; then echo ">> $logfile"; tail -n "${2:-100}" -f "$logfile"
        else echo 'Логи Lavalink не найдены: ни контейнер, ни systemd-юнит, ни каталог logs рядом с процессом' >&2; exit 1; fi
        ;;
      *) echo 'lava status|restart|logs [N]' >&2; exit 1 ;;
    esac
    ;;
  help) echo 'sbln: меню | panel [stable|proto] | status | logs [N] | start | stop | restart | update | prune [until|all] | lava status|restart|logs' ;;
  *) echo "Неизвестная команда: $cmd" >&2; exit 1 ;;
esac
