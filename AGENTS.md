# Agent Wrapper — AGENTS.md

This repository uses a canonical multi-agent control file:

```text
docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md
```

Read that file before any non-trivial task.

---

## Wrapper contract

`AGENTS.md` (this file) is a **thin compatibility wrapper** only. It must
**not** duplicate content from `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md`
or any other doc under `docs/arc/`. If a conflict ever appears between
this wrapper and the canonical file, the canonical wins — see
`docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md §1 ("Источник истины")`.

The other wrappers in this repo follow the same contract:

```text
AGENT.md   ← historical, deprecated alias
AGENTS.md  ← this file (current)
CLAUDE.md  ← Claude-specific thin wrapper (if present)
GEMINI.md  ← Gemini-specific thin wrapper (if present)
```

Any rule change, routing-table update, or workflow tweak belongs in
`docs/arc/`, **not** here.

For the canonical file's full content — project rules, routing table,
automation tools and the agent workflow — see the file linked above.

---

## Last verified

2026-07-17 — wrapper reviewed; `validate-docs.ps1` reports 0 issues and
0 warnings across all 9 consistency checks. No content drift between
this file and `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md`.
