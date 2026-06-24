# Changelog

## Unreleased

### Для пользователей

- **Копирование заказов:** добавлен пункт «Копировать» в контекстное меню списка «Заказы». Создаёт полную копию заказа с новым номером (например, «2-8» → «2-8.1»), статусом «Новый» и актуальной датой.

### Исправления

- Пока нет.

### Техническое

- Инициализирована система A.R.C. (Agent Reference & Control) — документация для AI-навигации, расчётов, релизов и автообновления.
- **Multi-agent control: portability migration.** Канонический master-файл перенесён из `~/.claude/skills/MULTI_AGENT_ARC_CALC_CONTROL.md` в `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md`. Теперь source of truth живёт внутри репозитория, версионируется в git и доступен любым агентам (Claude, Gemini, Copilot, Aider, CI). Файл `~/.claude/skills/MULTI_AGENT_ARC_CALC_CONTROL.md` стал тонким external bootstrap loader. Устранена циклическая переадресация `AGENT.md → MULTI → AGENT.md`. Wrappers (`AGENT.md`, `AGENTS.md`, `CLAUDE.md`, `GEMINI.md`) обновлены и более не дублируют правила.

---

## 3.35.0 — 2026-06-23

### Исправления

- Полный фикс утечки формул Anwis на не-Anwis товары.
- Исправлено: Откос материал, Работа, Пояс больше не показывают высоту 30 мм.
- Исправлено: редактирование ширины для Откос материал больше не добавляет +2 мм.
- Исправлено: смена режима AnwisSizeMode для не-Anwis товаров больше не портит размеры.
- Добавлены юнит-тесты на все 4 точки утечки.

---

*Полная история релизов доступна в `releases.json` и `MosquitoNetCalculator/Resources/update-log.json`.*
