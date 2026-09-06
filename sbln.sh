#!/usr/bin/env bash
set -euo pipefail

BASE=${SBLN_BASE:-/opt/sbln}
REPO=${SBLN_REPO:-https://github.com/byLucz/sblngavna.git}
CHANNEL=${SBLN_INSTANCE:-stable}
COMMAND=${1:-status}
if [[ $# -gt 0 ]]; then shift; fi
[[ "$CHANNEL" = stable || "$CHANNEL" = proto ]] || exit 1
[[ "$BASE" =~ ^/[a-zA-Z0-9_./-]+$ && "$BASE" != / && "$BASE" != *..* ]] || { echo 'Invalid SBLN_BASE' >&2; exit 1; }
STATE="$BASE/deployments/$CHANNEL"
CURRENT_FILE="$STATE/current"
CURRENT=''
NAME=sbln-bot
CONFIG="$BASE/config.json"
if [[ "$CHANNEL" = proto ]]; then NAME=sbln-bot-proto; CONFIG="$BASE/config-proto.json"; fi

validate_config() {
  python3 - "$CONFIG" "$CHANNEL" "$BASE/config.json" <<'PYCONFIG'
import json
from pathlib import Path
import sys


def validate(config, channel, production=None):
    system = config.get("System", {})
    for key in ("BotToken", "DbConnectionString"):
        value = str(system.get(key, "")).strip()
        if not value or any(marker in value for marker in ("ТЕСТОВЫЙ_ТОКЕН", "Uid=USER", "Pwd=PASS")):
            raise ValueError(f"Заполни System:{key}")
    for section, key in (("System", "MessagesFilePath"), ("Books", "BooksJsonPath")):
        value = str(config.get(section, {}).get(key, ""))
        if not value.startswith("/opt/sbln/data/") or ".." in value.split("/"):
            raise ValueError(f"{section}:{key} должен быть внутри /opt/sbln/data/")
    for key in ("PpmEnabled", "StreamsEnabled"):
        if str(system.get(key, "true")).lower() not in ("true", "false"):
            raise ValueError(f"System:{key} должен быть true или false")
    if channel == "proto":
        scope = str(system.get("SlashScopeGuild", "0"))
        if not scope.isdigit() or int(scope) == 0:
            raise ValueError("Для proto задай System:SlashScopeGuild тестового сервера")
        if production:
            prod = production.get("System", {})
            if system["BotToken"] == prod.get("BotToken"):
                raise ValueError("Proto должен использовать отдельный BotToken")
            if system["DbConnectionString"] == prod.get("DbConnectionString"):
                raise ValueError("Proto должен использовать отдельную БД")
    return str(system.get("PpmEnabled", "true")).lower() == "true"


if __name__ == "__main__":
    try:
        config = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
        production = None
        if sys.argv[2] == "proto" and Path(sys.argv[3]).is_file():
            production = json.loads(Path(sys.argv[3]).read_text(encoding="utf-8-sig"))
        print(str(validate(config, sys.argv[2], production)).lower())
    except (ValueError, OSError, KeyError) as error:
        print(f"Конфигурация не готова: {error}", file=sys.stderr)
        sys.exit(1)

PYCONFIG
}

compose() {
  local release=$1
  shift
  env -u SBLN_IMAGE -u SBLN_SOURCE -u SBLN_VERSION -u SBLN_CHANNEL -u SBLN_COMMIT \
    -u SBLN_CONTAINER -u SBLN_INSTANCE -u SBLN_CONFIG_FILE -u SBLN_DATA_DIR -u SBLN_LOG_DIR -u SBLN_AUDIO_ROOT \
    docker compose --project-name "sbln-$CHANNEL" --env-file "$release/.env" -f "$release/compose.yml" "$@"
}

require_current() {
  [[ -f "$CURRENT_FILE" ]] || { echo "Сначала install.sh $CHANNEL" >&2; exit 1; }
  CURRENT=$(cat "$CURRENT_FILE")
  [[ -f "$CURRENT/compose.yml" ]] || { echo "Сначала install.sh $CHANNEL" >&2; exit 1; }
}

activate() {
  local candidate=$1 previous='' legacy='' legacy_service=false
  if [[ -f "$CURRENT_FILE" ]]; then previous=$(cat "$CURRENT_FILE"); fi
  if [[ "$CHANNEL" = stable ]] && command -v systemctl >/dev/null && systemctl is-active --quiet sbln-service; then
    systemctl stop sbln-service
    legacy_service=true
  fi
  if [[ -z "$previous" ]] && docker container inspect "$NAME" >/dev/null 2>&1; then
    legacy="$NAME-previous-$(date +%s)"
    docker stop "$NAME" >/dev/null
    docker rename "$NAME" "$legacy"
  fi
  if compose "$candidate" up -d --no-build --wait --wait-timeout 120; then
    if [[ -n "$previous" ]]; then printf '%s\n' "$previous" > "$STATE/previous"; fi
    printf '%s\n' "$candidate" > "$STATE/current.next"
    mv -f "$STATE/current.next" "$CURRENT_FILE"
    if [[ "$CHANNEL" = stable ]] && command -v systemctl >/dev/null && systemctl is-enabled --quiet sbln-service; then
      systemctl disable sbln-service
    fi
    echo ">> $CHANNEL готов. Логи: $( [[ "$CHANNEL" = proto ]] && echo sblnproto || echo sbln ) logs"
    return
  fi
  echo 'Новый контейнер не готов; восстанавливаю предыдущий. Логи неудачного запуска:' >&2
  compose "$candidate" logs --tail 60 bot >&2 || true
  if [[ -n "$previous" ]]; then
    if ! compose "$previous" up -d --no-build --wait --wait-timeout 120; then
      echo "Восстановление тоже не прошло: $previous" >&2
    fi
  else
    compose "$candidate" down || true
    if [[ -n "$legacy" ]]; then docker rename "$legacy" "$NAME"; docker start "$NAME" >/dev/null; fi
  fi
  if [[ "$legacy_service" = true ]]; then systemctl start sbln-service; fi
  return 1
}

deploy() {
  local source=${1:-} version sha candidate channel_arg='' ppm
  for tool in docker git python3 tar flock; do
    command -v "$tool" >/dev/null || { echo "Нужен $tool" >&2; exit 1; }
  done
  docker info >/dev/null
  [[ $(docker compose up --help) == *--wait-timeout* ]] || { echo 'Обнови Docker Compose: нужен up --wait-timeout' >&2; exit 1; }
  mkdir -p "$STATE" "$BASE/data/$CHANNEL" "$BASE/logs/$CHANNEL" "$BASE/audio/$CHANNEL"
  exec 9>"$STATE/deploy.lock"
  flock -n 9 || { echo 'Другой деплой уже выполняется' >&2; exit 1; }
  candidate=$(mktemp -d "$STATE/release-XXXXXXXX")
  if [[ -n "$source" ]]; then
    source=$(cd -- "$source" && pwd)
    sha=$(git -C "$source" rev-parse HEAD)
    if [[ "$CHANNEL" = stable ]]; then
      [[ -z $(git -C "$source" status --porcelain) ]] || { echo 'Изменённый checkout можно тестировать через proto --local' >&2; exit 1; }
      version=$(git -C "$source" tag --points-at HEAD -l 'v*' | sed -nE 's/^v([0-9]+\.[0-9]+\.[0-9]+)$/\1/p' | sort -V | tail -1)
      [[ -n "$version" ]] || { echo 'Для stable нужен релизный тег; для стенда используй proto --local' >&2; exit 1; }
    fi
  else
    exec 8>"$BASE/repository.lock"
    flock 8
    if [[ ! -d "$BASE/repository/.git" ]]; then git clone --no-checkout "$REPO" "$BASE/repository"; fi
    git -C "$BASE/repository" fetch origin --tags
    if [[ "$CHANNEL" = stable ]]; then
      version=$(git -C "$BASE/repository" tag --merged origin/master -l 'v*' | sed -nE 's/^v([0-9]+\.[0-9]+\.[0-9]+)$/\1/p' | sort -V | tail -1)
      [[ -n "$version" ]] || { echo 'Нет релизного тега в master. Для теста: install.sh proto --local' >&2; exit 1; }
      sha=$(git -C "$BASE/repository" rev-parse "v$version^{commit}")
    else
      sha=$(git -C "$BASE/repository" rev-parse origin/proto)
    fi
    source="$candidate/source"
    mkdir "$source"
    git -C "$BASE/repository" archive "$sha" | tar -x -C "$source"
    flock -u 8
  fi
  [[ "$source" =~ ^/[a-zA-Z0-9_./-]+$ ]] || { echo 'Путь checkout должен быть без пробелов и специальных символов' >&2; exit 1; }
  if [[ "$CHANNEL" = proto ]]; then
    channel_arg=proto
    version=$(python3 -c 'import sys, xml.etree.ElementTree as E; print(E.parse(sys.argv[1]).findtext(".//SblnVersion"))' "$source/Directory.Build.props")
  fi
  [[ "$version" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]] || { echo 'Invalid version' >&2; exit 1; }
  if [[ ! -f "$CONFIG" ]]; then
    local example=config.example.json
    [[ "$CHANNEL" = stable ]] || example=config-proto.example.json
    install -m 0600 "$source/config/$example" "$CONFIG"
    echo ">> Создан $CONFIG. Заполни токен, БД и адрес Lavalink, затем повтори команду."
    exit 1
  fi
  chmod 600 "$CONFIG"
  ppm=$(validate_config)
  install -m 0600 "$CONFIG" "$candidate/config.json"
  cp "$source/docker-compose.yml" "$candidate/compose.yml"
  if [[ "$ppm" = true ]]; then
    [[ -S /var/run/docker.sock ]] || { echo 'Для PPM нужен /var/run/docker.sock' >&2; exit 1; }
    python3 -c 'import pathlib, sys; p=pathlib.Path(sys.argv[1]); s=p.read_text(); p.write_text(s.replace("    volumes:\n", "    volumes:\n      - /var/run/docker.sock:/var/run/docker.sock\n", 1))' "$candidate/compose.yml"
  fi
  cat > "$candidate/.env" <<EOF
SBLN_IMAGE=sbln-bot:$CHANNEL-${sha:0:12}-$(basename "$candidate")
SBLN_SOURCE=$source
SBLN_VERSION=$version
SBLN_CHANNEL=$channel_arg
SBLN_COMMIT=$sha
SBLN_CONTAINER=$NAME
SBLN_INSTANCE=$CHANNEL
SBLN_CONFIG_FILE=$candidate/config.json
SBLN_DATA_DIR=$BASE/data/$CHANNEL
SBLN_LOG_DIR=$BASE/logs/$CHANNEL
SBLN_AUDIO_ROOT=$BASE/audio
EOF
  compose "$candidate" config --quiet
  compose "$candidate" build --pull bot
  activate "$candidate"
}

case "$COMMAND" in
  deploy|update|hotswap) deploy "${1:-}" ;;
  rollback)
    require_current
    exec 9>"$STATE/deploy.lock"
    flock -n 9 || { echo 'Другой деплой уже выполняется' >&2; exit 1; }
    [[ -f "$STATE/previous" ]] || { echo 'Нет предыдущего релиза' >&2; exit 1; }
    activate "$(cat "$STATE/previous")"
    ;;
  status|pstatus) require_current; compose "$CURRENT" ps -a ;;
  logs) require_current; compose "$CURRENT" logs -f --tail "${1:-100}" bot ;;
  start) require_current; compose "$CURRENT" up -d --no-build --wait --wait-timeout 120 ;;
  stop|down) require_current; compose "$CURRENT" stop ;;
  restart) require_current; compose "$CURRENT" restart bot; compose "$CURRENT" up -d --no-build --wait --wait-timeout 120 ;;
  lava)
    lava=$(docker ps --format '{{.Names}}' | sed -n '/[Ll]avalink/{p;q;}')
    case "${1:-status}" in
      status) if [[ -n "$lava" ]]; then docker inspect --format '{{.State.Status}}' "$lava"; else systemctl status lavalink --no-pager; fi ;;
      restart) if [[ -n "$lava" ]]; then docker restart "$lava"; else systemctl restart lavalink; fi ;;
      *) echo 'lava status|restart' >&2; exit 1 ;;
    esac
    ;;
  help) echo 'status | logs [N] | start | stop | restart | update [checkout] | rollback | lava status|restart'; echo 'proto: hotswap [checkout], down, pstatus' ;;
  *) echo "Неизвестная команда: $COMMAND. Используй help." >&2; exit 1 ;;
esac
