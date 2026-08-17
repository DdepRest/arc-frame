# Agent Wrapper — AGENTS.md

The agent system lives in `agents/` (single folder):

```text
agents/README.md                         ← что это за система и как ей пользоваться
agents/docs/MULTI_AGENT_ARC_CALC_CONTROL.md  ← canonical source of truth (routing, CONTROL)
agents/docs/CHEATSHEET.md                ← быстрый вход (критические правила, читай первым)
agents/scripts/                          ← validate-docs.ps1, arc-check.ps1, what-to-update.ps1, gensymbols.ps1, render-matrix.ps1, sync-version.ps1
```

Read `agents/README.md` and then `agents/docs/MULTI_AGENT_ARC_CALC_CONTROL.md` before any non-trivial task.

---

## Wrapper contract

`AGENTS.md` (this file) is a **thin compatibility wrapper** only. It must
**not** duplicate content from `agents/docs/MULTI_AGENT_ARC_CALC_CONTROL.md`
or any other doc under `agents/docs/`. If a conflict ever appears between
this wrapper and the canonical file, the canonical wins — see
`agents/docs/MULTI_AGENT_ARC_CALC_CONTROL.md §1 ("Источник истины")`.

The other wrappers in this repo follow the same contract:

```text
AGENT.md   ← historical, deprecated alias
AGENTS.md  ← this file (current)
CLAUDE.md  ← Claude-specific thin wrapper (if present)
GEMINI.md  ← Gemini-specific thin wrapper (if present)
```

Any rule change, routing-table update, or workflow tweak belongs in
`agents/docs/`, **not** here.

---

## Self-maintenance duty (обязанность самоподдержания)

This wrapper is **part of the system and must be kept up to date by AI agents**,
like every file under `agents/docs/`. Any structural change (file splits,
renames/moves, new modules) or change to the control system itself must be
reflected here and in `agents/docs/` in the same work cycle. Rules, situations А–Ж
and enforcement — see `agents/docs/MULTI_AGENT_ARC_CALC_CONTROL.md` §13
(self-maintenance duty). Stale documentation actively misleads agents.

---

## Last verified

2026-08-10 — system moved into a single `agents/` folder (docs + scripts);
wrapper now points to `agents/README.md`. `agents/scripts/validate-docs.ps1` reports 0 issues.
