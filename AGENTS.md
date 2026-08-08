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

---

## Self-maintenance duty (обязанность самоподдержания)

This wrapper is **part of the system and must be kept up to date by AI agents**,
like every file under `docs/arc/`. Any structural change (file splits,
renames/moves, new modules) or change to the control system itself must be
reflected here and in `docs/arc/` in the same work cycle. Rules, situations А–Ж
and enforcement — see `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md` §13
(self-maintenance duty). Stale documentation actively misleads agents.

---

## Last verified

2026-08-03 — wrapper reviewed; self-maintenance duty section added (CONTROL §13);
`validate-docs.ps1` reports 0 issues (мягкие git-based warnings допустимы до коммита —
проверки #7/#8 сравнивают даты с git-историей).
