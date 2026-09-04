#!/usr/bin/env bash
# Управление ботом sbln: разворот через Docker (bot + lavalink) или локальный dotnet run.
# Секреты лежат ВНЕ репо. Дефолт-папка: /opt/sbln (файл secrets.json).
# Переопределить: ./sbln.sh --secrets /путь/до/папки <команда>  ИЛИ env SBLN_SECRETS_DIR.
set -euo pipefail

cd "$(dirname "$0")"

# --secrets <dir> перед командой
SECRETS_DIR="${SBLN_SECRETS_DIR:-/opt/sbln}"
if [[ "${1:-}" == "--secrets" ]]; then
  SECRETS_DIR="${2:?путь после --secrets обязателен}"
  shift 2
fi
SECRETS_FILE="$SECRETS_DIR/secrets.json"
SECRETS_EXAMPLE="config/secrets.example.json"

# docker compose v2 или v1
if docker compose version >/dev/null 2>&1; then
  DC="docker compose"
elif command -v docker-compose >/dev/null 2>&1; then
  DC="docker-compose"
else
  DC=""
fi

ensure_secrets() {
  if [[ ! -f "$SECRETS_FILE" ]]; then
    echo ">> $SECRETS_FILE нет — создаю из шаблона."
    mkdir -p "$SECRETS_DIR"
    cp "$SECRETS_EXAMPLE" "$SECRETS_FILE"
    chmod 600 "$SECRETS_FILE" 2>/dev/null || true
    echo ">> Заполни $SECRETS_FILE (BotToken, DbConnectionString, LavaPass;"
    echo ">> для docker: LavaHost=lavalink, MailImapHost=host.docker.internal) и запусти снова."
    exit 1
  fi
}

require_docker() {
  if [[ -z "$DC" ]]; then
    echo "!! docker compose не найден. Поставь Docker."
    exit 1
  fi
}

docker_up() {
  require_docker
  ensure_secrets
  echo ">> Секреты: $SECRETS_FILE"
  echo ">> Сборка и запуск (bot + lavalink)..."
  SBLN_SECRETS_DIR="$SECRETS_DIR" $DC up -d --build
  echo ">> Готово. Логи: ./sbln.sh logs"
}

dotnet_run() {
  ensure_secrets
  echo ">> Секреты: $SECRETS_FILE"
  echo ">> Локальный запуск через dotnet run."
  SBLN_SECRETS="$SECRETS_FILE" dotnet run -c Release
}

case "${1:-menu}" in
  docker)         docker_up; exit 0 ;;
  run|dotnet)     dotnet_run; exit 0 ;;
  restart-bot)    require_docker; SBLN_SECRETS_DIR="$SECRETS_DIR" $DC restart bot; exit 0 ;;
  restart-lava)   require_docker; SBLN_SECRETS_DIR="$SECRETS_DIR" $DC restart lavalink; exit 0 ;;
  logs)           require_docker; SBLN_SECRETS_DIR="$SECRETS_DIR" $DC logs -f --tail=100; exit 0 ;;
  stop|down)      require_docker; SBLN_SECRETS_DIR="$SECRETS_DIR" $DC down; exit 0 ;;
esac

while true; do
  cat <<MENU

=== sbln ===  (секреты: $SECRETS_FILE)
 1) Docker: собрать и запустить всё (bot + lavalink)
 2) Локально: dotnet run
 3) Перезапустить только бота
 4) Перезапустить только lavalink
 5) Логи (follow)
 6) Остановить всё (compose down)
 7) Сменить путь до секретов
 0) Выход
MENU
  read -rp "> " choice
  case "$choice" in
    1) docker_up ;;
    2) dotnet_run ;;
    3) require_docker; SBLN_SECRETS_DIR="$SECRETS_DIR" $DC restart bot ;;
    4) require_docker; SBLN_SECRETS_DIR="$SECRETS_DIR" $DC restart lavalink ;;
    5) require_docker; SBLN_SECRETS_DIR="$SECRETS_DIR" $DC logs -f --tail=100 ;;
    6) require_docker; SBLN_SECRETS_DIR="$SECRETS_DIR" $DC down ;;
    7) read -rp "Новая папка секретов: " d; SECRETS_DIR="${d:-$SECRETS_DIR}"; SECRETS_FILE="$SECRETS_DIR/secrets.json" ;;
    0) exit 0 ;;
    *) echo "?" ;;
  esac
done
