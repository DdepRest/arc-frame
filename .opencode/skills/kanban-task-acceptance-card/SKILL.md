---
name: kanban-task-acceptance-card
description: Use whenever the user asks to split, break down, decompose, or 拆解 one original task/card/requirement into multiple kanban todo cards and needs a final overall acceptance card, 验收卡片, 总体验收卡, acceptance card, or validation task. This skill creates the post-split quality gate card that preserves the original requirement, references the original source card when available, and verifies the implementation has not drifted.
---

# Kanban Task Acceptance Card

Use this skill whenever one original user request is decomposed into multiple kanban cards. The goal is to add a final acceptance card that checks the whole result against the original request, finds gaps, and protects engineering quality.

## When To Create It

Create exactly one acceptance card when:

- The user asks to split, break down, decompose, or 拆解 one task into multiple cards.
- The created cards are separate implementation steps for the same original requirement.
- The user asks to create multiple cards from a single feature, bug, refactor, or design request.

Do not create an acceptance card when:

- The user is adding unrelated independent tasks.
- The user explicitly says not to add review or acceptance work.
- There is already a clear overall acceptance card for the same original request in the new card set.

## Card Requirements

The acceptance card must be a normal todo card using the same `.kanban/board.json` card shape as `create-kanban`:

- `status`: `todo`
- `workflowMode`: `default`, unless the original request explicitly requires OpenSpec
- `running`: `false`
- `compacting`: `false`
- `lastRunDurationMs`: `0`

Use the same priority as the highest-priority split card unless the user specifies otherwise.

## Title Guidance

When one task is split into multiple cards, title every new split card with this structure:

```text
<任务主题> <执行顺序> <具体卡片名称>
```

Rules:

- `<任务主题>` is the shared short subject of the original requirement, such as `卡片筛选` or `Review 流程`.
- `<执行顺序>` is a plain number matching the intended execution order, starting at `1`.
- `<具体卡片名称>` is the real actionable card name, not a generic word like `任务` or `步骤`.
- Apply the same rule to the final acceptance card, using the next sequence number after the implementation cards.

Examples:

- `卡片筛选 1 增加搜索输入`
- `卡片筛选 2 接入列表过滤`
- `卡片筛选 3 总体验收`

For the final acceptance card, use a concise specific card name that clearly signals acceptance:

- Chinese: `验收<功能名>`, `总体验收<需求名>`, `验收拆分任务`
- English: `Accept <feature>`, `Validate <requirement>`, `Final acceptance`

## Summary Requirements

The summary must preserve enough original context that a later agent can validate the work without reading the original chat.

Include:

- Original user requirement or design intent.
- Reference to the original source card when the split came from an existing card, such as title, id, and `.kanban/board.json`; this is a lookup hint only, not a requirement to read the original card every time.
- List of split implementation cards by title when known.
- Relevant design, architecture, API, and test document paths, including the sections or contracts to verify. If no relevant document exists, explicitly record that absence and the documentation path that should be added or updated.
- Cross-system boundaries, handoffs, and shared contracts that must work end to end.
- Acceptance checks for functional correctness, edge cases, integration, tests, and user-facing behavior.
- Instruction to inspect the current implementation, identify missing or drifting parts, and add fixes if needed; consult the original source card only when the preserved summary is insufficient to confirm the requirement.
- Definition of done: original request is satisfied end-to-end and no obvious engineering-quality gaps remain.

## Recommended Summary Template

```text
目标：对原始需求“<original request>”做总体验收，确认端到端结果符合设计意图。
相关文档：<design/architecture/API/test document paths and relevant sections>；<if absent, state the documentation path to add or update>。
上下文与依赖：原始卡片参考：<source card title/id in .kanban/board.json, if any>；仅在需求判断不确定时回看原始卡片。已拆分的实现卡片包括：<card titles>。跨系统边界和共享契约：<systems, handoffs, and contracts>。
实施范围：检查当前实现是否仍符合原始需求和设计意图，重点验证功能完整性、边界情况、集成路径、错误处理、测试覆盖和用户可见行为；发现遗漏、跑偏或工程质量问题时补齐修复。
验证：执行<focused automated tests/build/manual flow>。完成标准：原始需求端到端成立，拆分实现之间协同一致，相关编译/测试通过，没有明显回归风险。
```

## Workflow With Card Creator

When used together with `create-kanban`:

1. First create the split implementation cards.
2. Name all split cards using `<任务主题> <执行顺序> <具体卡片名称>`.
3. Then append one acceptance card after those implementation cards, using the next sequence number in its title.
4. Keep all cards in `todo`; do not run them automatically.
5. Increment `.kanban/board.json` metadata once for the whole append operation.
6. In the final user response, list the acceptance card separately so the user can see the quality gate was added.

## Safety Checks

- Do not replace a concrete implementation card with only an acceptance card.
- Do not mark the acceptance card as review, done, archived, or running.
- Do not create multiple acceptance cards for the same decomposition unless the user asks for multiple independent acceptance gates.
- Preserve existing cards and sessions.
