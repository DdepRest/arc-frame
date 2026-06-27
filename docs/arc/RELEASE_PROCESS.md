# RELEASE_PROCESS.md

## Где хранится версия программы

**Единственный источник правды:** `MosquitoNetCalculator/MosquitoNetCalculator.csproj`

```xml
<Version>3.35.0</Version>
```

Всё остальное (AssemblyVersion, FileVersion, InformationalVersion) генерируется .NET SDK автоматически из этого поля.

---

## Какие файлы нужно менять при выпуске версии

### Обязательно:

1. **`MosquitoNetCalculator/MosquitoNetCalculator.csproj`** — поле `<Version>`.
2. **`releases.json`** — добавить новый релиз в начало массива `releases` и обновить поле `latest`.
3. **`MosquitoNetCalculator/Resources/update-log.json`** — добавить запись о новой версии.
4. **`CHANGELOG.md`** (в корне проекта) — добавить изменения в секцию `Unreleased` или создать новую версию.

### Рекомендуется:

5. **`docs/arc/CURRENT_STATE.md`** — обновить текущую версию и статус.
6. Проверить, что юнит-тесты проходят.

---

## Как выбирать номер версии

Формат: `MAJOR.MINOR.PATCH` (например, `3.35.0`).

| Тип изменения | Пример | Новая версия |
|---------------|--------|--------------|
| Исправление бага, стабильность | Фикс отображения версии | `3.35.0` → `3.35.1` |
| Новая функция, заметное улучшение UI | Новый контрол, новый товар | `3.35.0` → `3.36.0` |
| Изменение расчётов, крупный рефакторинг | Новая формула, breaking change | `3.35.0` → `4.0.0` |

**ВАЖНО:** Изменение расчётов = MAJOR bump + обязательное явное разрешение владельца.

---

## Как собрать проект

### Шаг 1: Сборка

```batch
build.bat
```

Что делает:
1. Чистит `bin/obj`.
2. Восстанавливает NuGet-пакеты.
3. Собирает Release с `PublishSingleFile=true`.
4. Копирует `prices.json`, `app_icon.ico`, `check-deps.bat`.
5. Создаёт `settings.json` с дефолтами.
6. Создаёт ZIP: `publish\ARC-Frame-X.Y.Z-full.zip`.
7. Запускает приложение.

### Шаг 2: Создание установщика (опционально)

```batch
compile-installer.bat
```

Требует Inno Setup 6. Создаёт `Output\setup.exe`.

### Шаг 3: Обновление releases.json

Запустить PowerShell-скрипт (или вручную):

```powershell
update-releases-json.ps1
```

Или вручную добавить запись в `releases.json`:
```json
{
    "version": "3.35.0",
    "date": "2026-06-23",
    "type": "Исправление",
    "title": "...",
    "changes": ["..."],
    "url": "https://github.com/DdepRest/arc-frame/releases/download/v3.35.0/ARC-Frame-3.35.0-full.zip",
    "size": 66659897,
    "sha256": "..."
}
```

---

## Как найти итоговый файл сборки

После `build.bat`:
- **EXE:** `publish\MosquitoNetCalculator.exe`
- **ZIP для GitHub:** `publish\ARC-Frame-X.Y.Z-full.zip`
- **Установщик:** `Output\setup.exe` (после `compile-installer.bat`)

---

## Какой файл прикреплять к GitHub Release

**Asset:** `ARC-Frame-X.Y.Z-full.zip`

Это ZIP, содержащий `MosquitoNetCalculator.exe` + зависимые DLL.

---

## Формат тега

**Tag:** `v3.35.0` (с префиксом `v`).

**Название релиза:** `Version 3.35.0` или краткое описание.

---

## Полный процесс релиза

### Подготовка (AI может сделать):

1. Определить текущую версию из `.csproj`.
2. Посмотреть `git log` с момента прошлого релиза.
3. Предложить новую версию с обоснованием.
4. Обновить `<Version>` в `.csproj`.
5. Обновить `releases.json` (добавить релиз, обновить `latest`).
6. Обновить `update-log.json`.
7. Обновить `CHANGELOG.md`.
8. Запустить `build.bat` — проверить, что сборка проходит.
9. Запустить тесты: `dotnet test`.
10. Вычислить SHA-256 ZIP-файла и вписать в `releases.json`.
11. Показать владельцу финальный отчёт.

### Публикация (только после явного разрешения владельца):

12. Коммит изменений (кроме `releases.json`).
13. Создать GitHub Release с тегом `vX.Y.Z`.
14. Загрузить `ARC-Frame-X.Y.Z-full.zip` как asset.
15. **Только после успешной загрузки ZIP** — закоммитить и запушить обновлённый `releases.json` в `main`.
16. Проверить, что старая версия видит новую через автообновление.

> **⚠️ Правило безопасности:** `releases.json` в ветке `main` является **триггером автообновления**. Как только новая запись попадает в `main`, старые программы могут увидеть обновление. Поэтому `releases.json` нельзя публиковать в `main` раньше, чем GitHub Release создан и ZIP-asset загружен. Иначе пользователи увидят "Доступно обновление", но скачать не смогут.

---

## Что делать, если автообновление не видит новую версию

1. Проверить, что `releases.json` закоммичен в `main`.
2. Проверить, что URL в `releases.json` правильный и файл доступен.
3. Проверить, что `latest` в `releases.json` совпадает с версией в `.csproj`.
4. Проверить, что SHA-256 в `releases.json` совпадает с реальным хешем ZIP.
5. Проверить, что GitHub Release не draft и не prerelease.
6. Проверить `UpdateService.CurrentVersion` через Debug-лог.

---

## Source files

- `MosquitoNetCalculator/MosquitoNetCalculator.csproj`
- `releases.json`
- `MosquitoNetCalculator/Resources/update-log.json`
- `build.bat`
- `compile-installer.bat`
- `update-releases-json.ps1`
- `extract-release-notes.ps1`

## Last verified

2026-06-25 (A.R.C. v4 — SYMBOL_INDEX, INTENTS, arc-check)
