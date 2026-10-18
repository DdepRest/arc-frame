# Agent Wrapper — CLAUDE.md

This repository uses a canonical multi-agent control file:

```text
agents/docs/MULTI_AGENT_ARC_CALC_CONTROL.md
```

Before any non-trivial task:

1. Read `agents/docs/MULTI_AGENT_ARC_CALC_CONTROL.md`.
2. Follow its routing rules.
3. Read `agents/docs/CURRENT_STATE.md`.

For calculation, price, dimensions, Anwis, quote, factory, installation, or totals tasks, also read:

```text
agents/docs/CALCULATION_LOGIC.md
agents/docs/CALCULATION_TEST_CASES.md
agents/docs/GOTCHAS.md
```

For release or auto-update tasks, also read:

```text
agents/docs/RELEASE_PROCESS.md
agents/docs/AUTO_UPDATE.md
```

---

## Ограничения

- This file is a **thin compatibility wrapper**, not the source of truth.
- Do not duplicate project rules here.
- If this file conflicts with `agents/docs/MULTI_AGENT_ARC_CALC_CONTROL.md`, the repository-local canonical file wins.
- To change project rules, edit `agents/docs/MULTI_AGENT_ARC_CALC_CONTROL.md`, not this file.

---

Если `agents/docs/MULTI_AGENT_ARC_CALC_CONTROL.md` недоступен — остановись и сообщи владельцу. Не придумывай правила.


# Task Management & TODO Guidelines
- When viewing, querying, adding, updating, or deleting project tasks or TODOs, ALWAYS use the TODO MCP tools (`todo_get_tasks`, `todo_add_tasks`, `todo_update_tasks`, `todo_delete_tasks`, `todo_clear_category`, `todo_move_category`) on server `todo-mcp` or `todo-extension`.
- NEVER edit the `.todo` file directly with file modification tools.
