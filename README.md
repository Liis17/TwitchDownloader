# 🎥 TwitchDownloader

Консольное приложение для автоматического скачивания трансляций с Twitch через Telegram-бота с поддержкой многопоточной загрузки.

## 🚀 Возможности

- 📥 **Автоматическое скачивание** стримов с отслеживаемых каналов
- 🎭 **Параллельная загрузка** видео и аудиопотоков
- 🤖 **Полное управление через Telegram-бота**:
  - Добавление/удаление отслеживаемых каналов
  - Ручная загрузка по ссылке
  - Просмотр активных загрузок
- 📁 **Автоматическая организация файлов**:
  - Именование по шаблону: `[канал]_[тип]_[GUID].расширение`
  - Сохранение в папку Downloads или указанный каталог
- 🔔 **Уведомления о статусе**:
  - Старт/завершение загрузки
  - Ошибки в реальном времени

## 🛠️ Требования

1. **Windows 10/11** (x64)
2. **[.NET 8 Runtime](https://dotnet.microsoft.com/download)**
3. **Права администратора** (для работы с процессами FFmpeg)
4. Обязательные утилиты в PATH:
   - [FFmpeg](https://ffmpeg.org/download.html)
   - [yt-dlp](https://github.com/yt-dlp/yt-dlp)

## ⚙️ Настройка

1. **Настройка бота**:
   ```bash
   # Создать файлы конфигурации
   echo "BOT_TOKEN" > token
   echo "YOUR_TELEGRAM_ID" > id
   ```
2. Запуск приложения:
   ```
   TwitchDownloader.CLI.exe [путь_для_сохранения]
   ```
4. Команды Telegram-бота:
   ```
   /start - Главное меню
   📃 Список каналов - Показать отслеживаемые каналы
   ➕ Добавить канал - Начать отслеживание нового канала
   ➖ Удалить канал - Удалить канал из списка
   ⏬ Скачать сейчас - Ручная загрузка по ссылке
   ```
## 🔄 Логика работы
1. Автоскачивание:
   - Проверка активных трансляций каждую минуту
   - Параллельная загрузка:
      - Видеопоток (самое высокое качество из доступных)
      - Аудиопоток 1 (оригинальный)
      - Аудиопоток 2 (резервный)
2. Ручная загрузка
3. Файловая структура:
   ```
   📂 Указанная_папка/
   └── 📄 channel_video_abc123.mp4
   └── 📄 channel_audio1_abc123.aac
   └── 📄 channel_audio2_abc123.aac
   ```
