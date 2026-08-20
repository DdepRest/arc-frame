# AI Defaults Policy — «Don't invent» audit

Single source of truth for AI default-value decisions. The runtime
policy lives in [`../../MosquitoNetCalculator/Services/AiPlanSafetyPolicy.cs`](../../MosquitoNetCalculator/Services/AiPlanSafetyPolicy.cs).
This document is the *why* and the explicit owner confirmations that
back the policy. It complements [`GOTCHAS.md`](GOTCHAS.md) (where the
behavioral rules live) and [`CALCULATION_LOGIC.md`](CALCULATION_LOGIC.md)
(product-side defaults).

## How the policy is enforced

Every plan-building pipeline runs `AiPlanSafetyPolicy.Classify` on the
candidate commands before the user sees a preview or the order is
touched. Four canonical paths must all agree:

1. **LLM plan-mode** — `AiCommandParser.TryParsePlanResponse` sets
   `plan.NeedsClarification` immediately.
2. **LLM legacy single-action** — `AiCommandParser.TryParse` returns
   the bare `Action`; the VM and validator both run the policy before
   showing the action.
3. **Clarification form submit** — `AiAssistantViewModel.SubmitClarificationForm`
   re-runs the policy after `TryBuildCommand`; by construction a
   complete form passes with `MissingField.None`, but the call is an
   audit-trail lock against regressions in form validation.
4. **Slash command router** — `/очистить` (`AiAssistantViewModel.HandleLocalRoute`,
   `RouteKind.ClearPlan`) runs `AiPlanValidator.Validate` so
   `NeedsClarification` is fresh.

The single source-of-truth predicate is
`AiPlanSafetyPolicy.NeedsClarification(commands, sourceUserText)`.

## What counts as «inventing critical data»

| Rule | Predicate | Why critical |
|---|---|---|
| Anwis mode | Anwis product + user did not name a mode | Profile affects price (±200 ₽) |
| Dimensions | Non-manual product with width ≤ 0 or height ≤ 0 | Cannot compute area / running price |
| Installation | Installation-applicable product + user did not name a mode | «С монтажом» vs «без монтажа» swings the total by hundreds of rubles |
| Update target | `UpdateItems` without a `product`/`category` | «Поставь везде без монтажа» would touch every row |

Priority order on collision (the first failing rule wins):
**Anwis mode > dimensions > installation > update target**.
This keeps the developer-facing rule (Anwis is the most fragile) on
top and matches the human-priority rule (don't ask for a tiny
install-mode clarification when dimensions are still missing).

## Tables of defaults — explicit owner decisions

Each row: a parameter the AI can leave unspecified, the current
default, the explicit safety verdict, and the action taken. The
«Owner confirmed» column is updated by `validate-docs.ps1` and
reflects the latest version. Items marked **DEFERRED** are waiting on
a concrete user decision documented in §"Open questions".

| Parameter | Default behaviour | Verdict | Action |
|---|---|---|---|
| Quantity | `1` (per `AiCommandParams`) | Safe | Keep — confirmed by golden-case corpus + ExistingAdd tests |
| Product type | Read from request | Safe when 1 family name matches; card when ambiguous | Keep — existing `AiClarificationForm.FilterProductsForRequest` |
| Anwis mode | `AnwisSizeService.DefaultMode` (ББ60) | **Inventing** — blocked by `IsMissingAnwisMode` | Keep — safety policy blocks the card path |
| Монтаж | `-1` (program default = predicate on `ProductCatalog`) | **Inventing** — blocked by `IsMissingInstallation` | Keep — safety policy blocks the card path |
| Глубина откоса | none — `CalcSlope` validator rejects depth ≤ 0 | Safe | Keep — `AiPlanValidator.ValidateCommand` already enforces |
| **Price** | `AiCommandParser.GetDefaultPrice` (per color) | Safe **per product**, but source was hard-coded twice | **Move to `AiFactsProvider` in stage 2** — keeps behavior, centralises the table |
| **Color** | First color in palette when not named | **Decision pending** | DEFERRED — see `Open questions` |

### Open questions (waiting on owner)

1. **Color default.** When a product has a single color (e.g.
   «Дверная сетка» → only Белый), defaulting is safe. When it has
   multiple colors («Отлив», «Козырёк», «Короб» — 4 colors), we
   silently pick the first one. Two options:
   - **Variant A** (conservative): show the clarification card whenever
     colors > 1 and the user did not name one. Maximum UX cost but fully
     matches «never invent».
   - **Variant B** (permissive): keep the first-color default and add
     a visible note in the preview «Цвет: Белый (по умолчанию)».
   - Until owner picks one, current behavior is preserved and the
     default is documented here. No silent change in either direction.

2. **UpdateItems for existing colors.** The card path does not yet
   trigger when the model updates a color without naming the target
   product name. **Decision pending**; documented in §"Open questions"
   to avoid surprise in production.

### What this policy does NOT cover

- **Server-side defaults** like NVIDIA/OpenRouter quotas — outside the
  safety policy. See [`AUTO_UPDATE.md`](AUTO_UPDATE.md).
- **Prices** — the table itself is canonicalised in stage 2
  (`AiFactsProvider`); the policy answers what counts as safe to assume
  (no price invention; the catalog is the source).
- **Calculation rules** themselves — those live in
  [`CALCULATION_LOGIC.md`](CALCULATION_LOGIC.md) and
  [`GOTCHAS.md`](GOTCHAS.md).

## Revision log

- 2026-08-20 — created for hardening stage 1
  (`docs/plans/project-hardening-plan.md` §4). Baseline captured from
  existing behavior + golden corpus. Color row marked DEFERRED.
