#!/usr/bin/env bash
# Обновление развёртывания: git pull из master, пересборка образа и перезапуск контейнера.
# Запуск из корня репозитория: ./update.sh
# Данные контейнера (docker/data, docker/downloads, docker/.env) не затрагиваются.

set -Eeuo pipefail

# Весь код внутри main: bash разбирает функцию целиком до выполнения,
# поэтому git pull, меняющий этот файл, не ломает уже запущенный скрипт.
main() {
    local -r branch="master"
    local -r compose_file="docker/docker-compose.yml"

    log() {
        printf '[update] %s\n' "$*"
    }

    fail() {
        log "ОШИБКА: $*" >&2
        exit 1
    }

    command -v git >/dev/null 2>&1 || fail "Команда git не найдена."
    command -v docker >/dev/null 2>&1 || fail "Команда docker не найдена."

    local project_dir
    project_dir="$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")" && pwd)"
    cd "$project_dir"

    [[ -e .git ]] || fail "Каталог $project_dir не является Git-репозиторием."
    [[ -f "$compose_file" ]] || fail "Не найден файл $compose_file."
    [[ -f docker/.env ]] || fail "Не найден docker/.env. Создайте его из docker/.env.example."

    local current_branch
    current_branch="$(git branch --show-current)"
    [[ "$current_branch" == "$branch" ]] || fail "Ожидалась ветка $branch, сейчас выбрана $current_branch."

    git remote get-url origin >/dev/null 2>&1 || fail "Не найден Git remote origin."

    # docker/data, docker/downloads и docker/.env игнорируются Git и сюда не попадают.
    local dirty
    dirty="$(git status --porcelain --untracked-files=all)"
    if [[ -n "$dirty" ]]; then
        printf '%s\n' "$dirty" >&2
        fail "В репозитории есть незакоммиченные изменения (список выше). Сначала сохраните их или уберите."
    fi

    local old_commit new_commit
    old_commit="$(git rev-parse --short HEAD)"
    log "Обновляю ветку $branch из origin..."
    git pull --ff-only origin "$branch"
    new_commit="$(git rev-parse --short HEAD)"

    if [[ "$old_commit" == "$new_commit" ]]; then
        log "Новых коммитов нет; образ всё равно будет пересобран."
    else
        log "Репозиторий обновлён: $old_commit -> $new_commit."
    fi

    local -a compose=(docker compose -f "$compose_file")

    # Сборка идёт, пока работает прежний контейнер: при ошибке сборки он не останавливается.
    log "Собираю новый Docker-образ..."
    "${compose[@]}" build --pull || fail "Сборка не удалась; прежний контейнер продолжает работать."

    # Volumes не удаляются: настройки, история записей и записи остаются в docker/data и docker/downloads.
    log "Перезапускаю контейнер с новым образом..."
    "${compose[@]}" up -d --force-recreate --remove-orphans

    log "Готово. Текущее состояние:"
    "${compose[@]}" ps
}

main "$@"
exit
