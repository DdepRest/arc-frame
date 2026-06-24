# Agent Wrapper — AGENTS.md

This repository uses a canonical multi-agent control file:

```text
docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md
```

Before any non-trivial task:

1. Read `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md`.
2. Follow its routing rules.
3. Read `docs/arc/CURRENT_STATE.md`.

For calculation, price, dimensions, Anwis, quote, factory, installation, or totals tasks, also read:

```text
docs/arc/CALCULATION_LOGIC.md
docs/arc/CALCULATION_TEST_CASES.md
docs/arc/GOTCHAS.md
```

For release or auto-update tasks, also read:

```text
docs/arc/RELEASE_PROCESS.md
docs/arc/AUTO_UPDATE.md
```

---

## Ограничения

- This file is a **thin compatibility wrapper**, not the source of truth.
- Do not duplicate project rules here.
- If this file conflicts with `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md`, the repository-local canonical file wins.
- To change project rules, edit `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md`, not this file.

---

Если `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md` недоступен — остановись и сообщи владельцу. Не придумывай правила.
