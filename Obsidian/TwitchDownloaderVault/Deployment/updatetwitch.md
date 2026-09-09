# updatetwitch

Parent: [[Index]]

## Назначение

`scripts/updatetwitch` обновляет серверное развёртывание TwitchDownloader одной
командой. Скрипт рассчитан на `/opt/TwitchDownloader`, ветку `master` и Docker
Compose-файл `docker/docker-compose.yml`.

## Файлы

- `scripts/updatetwitch` — Bash-скрипт обновления.
- `docker/docker-compose.yml` — Compose-сервис, которым управляет скрипт.
- `docker/.env` — обязательная локальная конфигурация, не коммитится.
- `docker/README.md` — установка симлинка и пользовательская инструкция.

## Ключевые функции

| Функция | Описание |
|---------|----------|
| `log(message: string): void` | Печатает сообщение с префиксом `[updatetwitch]`. |
| `fail(message: string): never` | Печатает ошибку и завершает скрипт с кодом 1. |
| `restore_previous_container(): never` | При ошибке после остановки пытается запустить прежний контейнер и сохраняет код ошибки. |
| `trap restore_previous_container ERR` | Подключает восстановление к ошибкам сборки и запуска. |

## Поток выполнения

1. Проверяются `git`, `docker`, каталог репозитория, Compose-файл и `docker/.env`.
2. Проверяется текущая ветка `master`, remote `origin` и отсутствие локальных
   изменений.
3. Выполняется `git pull --ff-only origin master`.
4. Compose останавливает контейнер и отмечает, что прежний экземпляр нужно
   восстановить при ошибке.
5. Образ пересобирается с `docker compose ... build --pull`.
6. Контейнер запускается через `up -d --force-recreate --remove-orphans`.
7. После успешного запуска trap отключается и выводится `docker compose ps`.

## Важные детали

- Скрипт намеренно не принимает путь проекта или ветку через environment: значения
  зафиксированы в `PROJECT_DIR` и `BRANCH`.
- При ошибке `build` или `up` вызывается `docker compose start`; volumes с
  `Data/settings.data` и скачанными файлами не удаляются.
- Незакоммиченные изменения блокируют обновление, поэтому локальные правки не
  перезаписываются во время деплоя.

## Зависимости

- Использует: [[api/docker]], `git`, Docker Compose.
- Используется в: [[Index]], [[Архитектура]].
