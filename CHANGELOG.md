# Changelog

## 3.36.2 — 2026-06-25

### Улучшения

- **Брус, Пояс, Доставка — только сумма:** для этих товаров скрыты колонки «Кол-во», «Площ./Дл.» в таблице расчёта и отключены поля «Кол-во», «Ширина», «Высота» в панели быстрого ввода. Превью показывает только цену.

### Исправления

- Исправлена кодировка UTF-8 BOM в PowerShell-скриптах (`validate-docs.ps1`, `what-to-update.ps1`, `render-matrix.ps1`, `generate-update-log.ps1`) — кириллица в regex парсилась некорректно.
- Восстановлен `update-log.json` из git (был обрезан до 3 записей); добавлена запись 3.36.1.
- Исправлено 6 ошибок в `UpdateLogTests` и `ManualChecklistTests`.

---

## 3.36.1 — 2026-06-24

### Техническое

- **A.R.C. upgrade v3** — полная автоматизация документирования (0 ручных операций для агента):
  - `docs/arc/documentation-matrix.json` — машиночитаемый источник матрицы «файл → документы».
  - `what-to-update.ps1` — скрипт: принимает `git diff --name-only`, выводит список docs/arc файлов к обновлению. Фаза Document стала 100% механической.
  - `generate-update-log.ps1` — автогенерация `update-log.json` из `CHANGELOG.md`. Убирает ручную синхронизацию при релизе.
  - `render-matrix.ps1` — генерация `DOCUMENTATION_MATRIX.md` из JSON.
  - `validate-docs.ps1` расширен до 8 проверок: git-based Last verified (check 7), staleness detection (check 8).
  - `PROMPTS.md` — добавлен Prompt 7 (self-check после изменений: git diff → what-to-update → validate-docs).
  - `AGENTS.md` — добавлен inline routing (таблица в самом wrapper'е — агент может не читать CHEATSHEET для routing).
  - `CHEATSHEET.md` — добавлено правило #17 (what-to-update → validate-docs) и секция «Инструменты автоматизации».

- **A.R.C. upgrade v2** — CHEATSHEET.md, DOCUMENTATION_MATRIX.md, PROMPTS.md, гранулярный routing, validate-docs.ps1.

---

## 3.36.0 — 2026-06-24

### Для пользователей

- **Копирование заказов:** добавлен пункт «Копировать» в контекстном меню списка «Заказы». Создаёт полную копию заказа с новым номером (например, «2-8» → «2-8.1»), статусом «Новый» и актуальной датой.

### Исправления

- Исправлен XML-комментарий `InstallationSurcharge` в `OrderItem.Installation.cs`: `Default 0 ₽` → `Default 500 ₽` (соответствует коду).
- Исправлен pre-existing дрифт версии в тесте `UpdateLogTests.AllNewestFirst_FirstItemIsNewest` (ожидал 3.34.5, теперь 3.35.0).

### Техническое

- **GenerateCopyContractNumber** перенесён из `MainWindow.Orders.cs` в `OrderStorageService.cs`.
- Добавлено **11 тестов**.
- Инициализирована система A.R.C.
- **Multi-agent control: portability migration.**

---

## 3.35.0 — 2026-06-23

### Исправления

- Полный фикс утечки формул Anwis на не-Anwis товары.

---

*Полная история релизов доступна в `releases.json` и `MosquitoNetCalculator/Resources/update-log.json`.*
