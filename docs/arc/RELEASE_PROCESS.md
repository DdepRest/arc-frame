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

## Канонический Pipeline релиза (выучить один раз)

> **⚠️ Правило безопасности:** `releases.json` в ветке `main` является **триггером автообновления**. Как только новая запись попадает в `main`, старые программы могут увидеть обновление. Поэтому `releases.json` нельзя публиковать в `main` раньше, чем GitHub Release создан и ZIP-asset загружен. Иначе пользователи увидят «Доступно обновление», но скачать не смогут.

---

## Канонический Pipeline релиза (выучить один раз)

Единственная причина, по которой этот pipeline нарушается — попытки «пропустить шаги ради скорости». Не пропускай. Один релиз в неделю — это нормально, два сломанных CDN-кэша за день — нет.

### Этап 1 — Подготовка (автоматизируемо)

| # | Действие | Команды |
|---|----------|---------|
| 1.1 | Определить текущую версию | `grep '<Version>' MosquitoNetCalculator/MosquitoNetCalculator.csproj` |
| 1.2 | Посмотреть историю с прошлого релиза | `git log vX.Y.Z..HEAD --oneline` |
| 1.3 | Предложить новую версию (с обоснованием) | См. раздел «Как выбирать номер версии» |
| 1.4 | Обновить `<Version>` в `.csproj` | `str_replace` |
| 1.5 | Обновить `CHANGELOG.md` | секция `Unreleased` → `## X.Y.Z — YYYY-MM-DD` |
| 1.6 | Обновить `update-log.json` (только пользовательские изменения, без техжаргона) | ручная правка |
| 1.7 | Обновить `releases.json` (полная запись version+date+type+title+changes+url+size+sha256) | ручная правка |
| 1.8 | Обновить `docs/arc/CURRENT_STATE.md` | версия+Last verified |
| 1.9 | Запустить тесты | `dotnet test MosquitoNetCalculator.sln -c Release` |
| 1.10 | Показать владельцу финальный отчёт | ждать явного подтверждения |

### Этап 2 — Сборка (автоматизируемо)

| # | Действие | Команды |
|---|----------|---------|
| 2.1 | Очистить bin/obj, publish | `rmdir /s /q publish MosquitoNetCalculator\bin MosquitoNetCalculator\obj` |
| 2.2 | Restore + publish single-file | `dotnet publish MosquitoNetCalculator/MosquitoNetCalculator.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish` |
| 2.3 | Скопировать resources в publish | `cp MosquitoNetCalculator/prices.json MosquitoNetCalculator/Resources/app_icon.ico check-deps.bat publish/` + создать `publish/settings.json` |
| 2.4 | Создать ZIP с .exe + DLL | `powershell -NoProfile -Command "Compress-Archive -Force -Path 'publish\MosquitoNetCalculator.exe','publish\*.dll' -DestinationPath 'publish\ARC-Frame-X.Y.Z-full.zip'"` |
| 2.5 | Вычислить SHA-256 | `certutil -hashfile 'publish\ARC-Frame-X.Y.Z-full.zip' SHA256` |
| 2.6 | Вычислить размер файла | `(Get-Item 'publish\ARC-Frame-X.Y.Z-full.zip').Length` |
| 2.7 | Вписать size+SHA-256 в `releases.json` | ручная правка |

**ВАЖНО:** `Build.bat` запускает `start ""` для приложения в конце — это неудобно для batch-релизов. Используй прямые команды выше вместо `build.bat`.

### Этап 3 — Публикация GitHub Release (только после явного разрешения владельца)

| # | Действие | Команды |
|---|----------|---------|
| 3.1 | Коммит кода+документации (БЕЗ releases.json) | `git add <.csproj, CHANGELOG.md, update-log.json, источники, CURRENT_STATE.md>` |
| 3.2 | Push в main (см. ниже про rebase sequence) | `git pull --rebase origin main && git push origin main` |
| 3.3 | Подготовить release notes | в `/tmp/release-notes.md` (отдельный .md файл) |
| 3.4 | Создать GitHub Release с ZIP | `gh release create vX.Y.Z 'publish/ARC-Frame-X.Y.Z-full.zip' --title 'Version X.Y.Z' --notes-file /tmp/release-notes.md` |
| 3.5 | Проверить, что Release не draft | `gh release view vX.Y.Z --json isDraft,isPrerelease` |

**Если `gh release create` падает с TLS timeout:**
1. Сначала проверить соединение: `curl -I https://api.github.com`
2. Если timeout — подождать 1-2 мин и повторить
3. Если всё равно падает — создать Release через Web UI: https://github.com/DdepRest/arc-frame/releases/new

### Этап 4 — Push `releases.json` (ТОЛЬКО после успешного шага 3.4)

| # | Действие | Команды |
|---|----------|---------|
| 4.1 | Подтвердить, что GitHub Release существует и ZIP загружен | `gh release view vX.Y.Z --json url,assets` |
| 4.2 | Добавить releases.json | `git add releases.json` |
| 4.3 | Коммит + push | `git commit -m "release: update releases.json for vX.Y.Z" && git push origin main` |

**И только после этого** старые программы увидят новое обновление.

### Git push sequence (если remote отстаёт или есть конфликт)

Частая ошибка: после коммита кода+документации `git push` падает с `error: failed to push some refs`. Это значит, что в remote main есть коммиты, которых нет локально (например, другой процесс влил что-то в main параллельно). Решение:

```bash
# 1. Сохранить uncommitted changes (включая releases.json)
git stash
# 2. Подтянуть remote и перенести локальные коммиты поверх
git pull --rebase origin main
# 3. Запушить
git push origin main
# 4. Восстановить stashed changes (включая releases.json, ЕСЛИ ещё не опубликован Release)
git stash pop
```

Если на шаге 4 `stash pop` вызывает merge-конфликт в `releases.json`, это значит, что remote main всё ещё содержит предыдущую версию `releases.json`. Решение:

```bash
# releases.json в сташе — это всегда более новая (правильная) версия
git checkout --theirs releases.json
git add releases.json
git commit -m "release: update releases.json for vX.Y.Z"
```

**НИКОГДА не делай `git checkout --ours releases.json`** — это сохранит remote-версию (старую).

### Чеклист «выполнено всё»

После всех 4 этапов убедись:

- [ ] `version` в `.csproj` = X.Y.Z
- [ ] `latest` в `releases.json` = X.Y.Z → git push выполнен
- [ ] `releases.json` в remote main содержит sha256 = реальный
- [ ] GitHub Release `vX.Y.Z` опубликован (не draft, не prerelease)
- [ ] ZIP-asset скачивается через URL из `releases.json`
- [ ] Старая версия 3.40.4 (предыдущая) видит X.Y.Z через «Проверить обновления»

---

## ⏱ Почему пользователь может видеть «обновлений нет» еще 5-15 мин

После пуша `releases.json` в `main` GitHub CDN-кэш на `raw.githubusercontent.com` может отдавать старую версию файла **5-15 минут** (иногда дольше). Это нормальное поведение CDN, не баг. Если пользователь старой версии сообщает «программа не детектит новую версию» сразу после релиза:

1. Сначала проверить, что все 4 этапа завершены.
2. Диагностика «не видит обновление» — см. AUTO_UPDATE.md.
3. Подождать 5-15 мин для CDN-кэша.
4. Перезапуск программы пользователем сбрасывает HttpClient-кэш.

---

## Что делать, если автообновление не видит новую версию

1. Проверить, что `releases.json` закоммичен в `main`.
2. Проверить, что URL в `releases.json` правильный и файл доступен.
3. Проверить, что `latest` в `releases.json` совпадает с версией в `.csproj`.
4. Проверить, что SHA-256 в `releases.json` совпадает с реальным хешем ZIP.
5. Проверить, что GitHub Release не draft и не prerelease.
6. Проверить `UpdateService.CurrentVersion` через Debug-лог.
7. **Проверить CDN-кэш** (см. AUTO_UPDATE.md раздел «Диагностика «не видит обновление»»):
   ```bash
   # Проверить через raw.githubusercontent.com (CDN-кэшировано):
   curl -s "https://raw.githubusercontent.com/DdepRest/arc-frame/main/releases.json" | python -c "import json,sys; print(json.load(sys.stdin)['latest'])"
   # Проверить через api.github.com (НЕ кэшируется):
   curl -s "https://api.github.com/repos/DdepRest/arc-frame/contents/releases.json" | python -c "import json,sys,base64; print(json.loads(base64.b64decode(json.load(sys.stdin)['content']))['latest'])"
   ```
   Если `raw` отдаёт старую версию, а `api` — новую: **CDN-кэш**. Подождать 5-15 мин.

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

2026-07-01 (v3.41.0 release: 4-stage pipeline + git push sequence + CDN-cache diagnostic systematized after real release run)
