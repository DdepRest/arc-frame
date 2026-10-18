---
name: create-kanban
description: Create one or more Kanban cards for this VSCODE_KANBAN extension and place them in the board todo list. Use this whenever the user asks to create kanban cards, split work into cards, add tasks to todo, put requirements onto the extension board, or turn a request into actionable cards for this repository, even if they do not explicitly mention the skill name.
license: MIT
compatibility: Requires Node.js and a VSCODE_KANBAN workspace.
metadata:
  author: VSCODE_KANBAN
  version: "1.1"
---

Create cards that match this extension's board format and add them to the To-Do list.

The extension stores cards in `.kanban/board.json` under `boardState.cards`. A To-Do card is a `CardRecord` with `status: "todo"`. New cards should be inserted at the front of `boardState.cards`, matching the extension's `orchestrateAddCard` behavior.

**Input**: The user's request may describe one card, multiple tasks, a feature to split into cards, or a backlog to add.

**Primary decomposition goal**: Reduce the implementation and verification complexity of every individual card so one agent can complete it reliably in one focused execution. Prefer an additional cohesive implementation card over a broad card that combines several uncertain changes. Do not create artificial micro-cards merely to increase card count.

**Steps**

1. **Understand the requested work**
   - Before deciding the card count, identify the affected systems, their integration boundaries, state/data contracts, likely verification method, and the relevant design, architecture, API, or test documents. Inspect the repository and the source card when available; do not guess the scope from the user wording alone.
   - Classify the work using the decomposition matrix below. Difficulty is determined by implementation and verification complexity, not by the number of words in the request.
   - Convert the request into one or more concrete implementation cards. A card must have one cohesive deliverable, a clear owner system, a single clear completion condition, and an independently executable verification step. Apply the card complexity gate before accepting a card as final.
   - If one original task is split into multiple implementation cards, also create one overall acceptance card using `kanban-task-acceptance-card` guidance and append it after the split cards.
   - Ask one short clarification only if the requested outcome is too vague to create actionable cards.
   - Do not split merely by generic phases such as "analysis", "coding", and "testing", and do not make a separate card for every file. Split at durable functional, contract, or system boundaries.

### Decomposition Matrix

| Scope | Difficulty | Required decomposition |
| --- | --- | --- |
| One localized, low-risk change with a known implementation and one direct verification path | `简单` | Keep as one card. Do not split it only to create process work. |
| One subsystem, or a bounded change spanning a few tightly coupled files, with limited state or compatibility risk | `标准` | Use one card when it remains independently verifiable; split into two cards when implementation and integration/verification form separate cohesive deliverables. |
| Multiple systems, scene/UI plus gameplay/data/save/network boundaries, public contracts, migrations, complex state, or a change whose regression risk cannot be verified in one focused flow | `困难` | Split into multiple ordered, verifiable cards. Each card must reduce complexity at one boundary; no implementation card may contain more than one system integration boundary unless they are inseparable. |

### Cross-System Split Rules

For cross-system work, normally create cards in the execution order that fits the request:

1. Define or update shared contracts, data models, configuration, migration, or architecture-facing interfaces when another system depends on them.
2. Implement each independently testable producer or owner subsystem.
3. Implement the consumer-side integration, orchestration, or UI/scene wiring.
4. Add or update focused automated tests, compatibility checks, and documentation when these do not naturally belong to the owning implementation card.
5. Add the required overall acceptance card.

Omit steps that have no concrete work. Combine adjacent steps only when they are owned by the same subsystem and can be verified through the same focused test or manual flow. Do not leave a card that says only "integrate all systems"; name the exact systems and contract it integrates.

### Card Complexity Gate

Before finalizing a card, check whether it can be implemented, reviewed, and verified in one focused execution without requiring the agent to hold several unrelated systems in context. Split the card when any of the following is true:

- It changes more than one independently owned subsystem, except for a narrowly defined producer-consumer handoff.
- It crosses two or more integration boundaries, such as UI-to-gameplay and gameplay-to-save-data.
- It combines a new contract/data migration with all producer, consumer, and UI/scene implementation work.
- It requires multiple unrelated verification flows to prove correctness.
- Its summary needs several unrelated deliverables joined by "and", or cannot state a single clear completion condition.

When a card is split, each resulting card must retain a meaningful, shippable intermediate outcome: a defined contract, a verified subsystem behavior, a tested integration path, or a focused regression suite. Keep tightly coupled edits together only when splitting them would prevent either card from being independently verified.

2. **Shape each card**
   - `title`: must end with a difficulty marker in the format `<specific title>（<difficulty>）`. `<difficulty>` must be exactly one of `困难`, `标准`, or `简单`; for example, `增加设置校验（标准）`.
   - Assign difficulty from the decomposition matrix. Use `简单` only for direct, low-risk localized work; use `标准` for bounded single-subsystem work; use `困难` for cross-system, complex-state, compatibility-sensitive, or high-risk work. Do not use `标准` as an unexamined default.
   - When splitting one original task into multiple cards, title every split card as `<任务主题> <执行顺序> <具体卡片名称>（<难度>）`, for example `卡片筛选 1 增加搜索输入（标准）`.
   - `summary`: must be self-contained enough for a build agent to execute without rereading the full conversation. Use all of the following labeled sections, with concrete paths, symbols, or names wherever they are known:
     ```text
     目标：<this card's single deliverable and non-goals>。
     相关文档：<res:// or repository documentation paths and the specific sections to consult>; if no relevant document exists, state “未发现，需在 <path> 补充/确认”。
     上下文与依赖：<affected system, existing behavior, upstream/downstream cards, source-card reference, contracts, data/scene/resource paths>。
     实施范围：<concrete implementation work and explicit boundary with adjacent cards>。
     验证：<focused automated test, build command, editor/manual flow, and expected result>。
     ```
   - Do not write placeholders such as "查看相关文档" or "处理上下文". If repository inspection found no applicable document, record that fact and name the document location that should be created or updated.
   - For every split card, state which prior card outputs it consumes and which following card it enables. The first card must state its external prerequisites; the final implementation card must state the handoff to overall acceptance.
   - `priority`: use `p4` by default. Use `p1`/`p2` only for urgent or blocking work, `p6`/`p7` for cleanup or optional work.
   - Do not create cards with an empty `title` or empty `summary`; the extension rejects those through its normal add-card path.

3. **Inspect the board immediately before writing**
   - Read `.kanban/board.json` from the current workspace when it exists.
   - If `.kanban/board.json` is missing, create the standard V1 wrapper with empty `boardState.cards` and `boardState.sessions`.
   - Preserve every existing card, session, and unknown field.
   - Do not edit active cards except by inserting new To-Do cards before them.
   - Treat the user's todo list as `boardState.cards` filtered by `status` values shown in the active To-Do panel, with new work using `status: "todo"`.

4. **Write cards with the bundled script**
   - Create a temporary JSON file containing an array of cards:
     ```json
     [
       {
         "title": "实现设置校验（标准）",
         "summary": "Add validation for the new settings field, update configuration docs, and run npm run compile.",
         "priority": "p4"
       }
     ]
     ```
   - Run:
     ```bash
     node .opencode/skills/create-kanban/scripts/add-cards.mjs --input <cards-json> --board .kanban/board.json
     ```
   - The script reads `.kanban/settings.json` and copies `settings.workflow.runnerProfiles.build.model` into each new card's `executeModel`, including `reasoningDepth` and `reasoningDepthMode` when present. This matches the extension's build profile default behavior.
   - The script inserts valid To-Do cards, updates `meta.updatedAt`, increments `meta.revision`, and keeps existing board data intact.

5. **Verify the result**
   - Re-read `.kanban/board.json` or inspect the diff.
   - Confirm each new card has:
     - `status: "todo"`
     - `workflowMode: "default"`
     - `executeModel` matches the build profile model/depth from `.kanban/settings.json` when configured
     - `running: false`
     - `compacting: false`
      - `lastRunDurationMs: 0`
      - a non-empty `title` and `summary`
      - a title that ends with `（困难）`, `（标准）`, or `（简单）`
      - `summary` contains concrete `目标`、`相关文档`、`上下文与依赖`、`实施范围`、`验证` sections
      - every cross-system split card names its system boundary, card dependencies, and focused verification path
   - Do not run `npm run compile` for board-only changes unless code files were also changed.

**Card JSON written by the script**

Each inserted card uses this extension-compatible shape:

```json
{
  "id": "<date-base36>-<random>",
  "title": "...",
  "priority": "p4",
  "summary": "...",
  "status": "todo",
  "workflowMode": "default",
  "createdAt": 1780000000000,
  "updatedAt": 1780000000000,
  "running": false,
  "compacting": false,
  "lastRunDurationMs": 0,
  "executeModel": {
    "providerID": "jws",
    "modelID": "gpt-5.5",
    "reasoningDepth": "medium",
    "reasoningDepthMode": "variant"
  }
}
```

**Output**

After writing the board, respond briefly:

```markdown
已创建 <N> 张 todo 卡片：
- <title 1> (<priority>)
- <title 2> (<priority>)
```

If no cards were written, say why and what is needed next.

When an acceptance card was added, list it separately so the user can see the quality gate was included.

**Priority Mapping**

Use this mapping unless the user gives an explicit priority:

- `p1`: production outage, data loss, security issue, blocking all work.
- `p2`: urgent bug or release blocker.
- `p3`: important user-visible feature or important bug.
- `p4`: normal feature, refactor, documentation, or unspecified task.
- `p5`: nice-to-have improvement.
- `p6`: low-priority cleanup.
- `p7`: someday/backlog idea.

**Guardrails**

- Do not overwrite `.kanban/board.json` from stale context; always read it immediately before writing.
- Do not create `done`, `in_progress`, `failed`, or `in_review` cards for new user requests.
- Do not modify `boardState.sessions` when only adding To-Do cards.
- Do not use internal TodoWrite as the final destination; TodoWrite may track your work, but the requested todo list is the extension board.
- Do not edit `.kanban/sessions/` for plain todo card creation.
- Do not run the card automatically unless the user explicitly asks to run it.
- If another card is currently running, still adding a `todo` card is safe because it only queues future work.
- When decomposing one original requirement into multiple cards, do not omit the final overall acceptance card unless the user explicitly opts out.
- Never omit, alter, or use a value outside `困难`, `标准`, or `简单` for the title difficulty marker.
