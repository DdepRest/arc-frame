# BRIEFING — 2026-11-02T12:54:00Z

## Mission
Implement Milestone 4 (Requirement R5: Interactive Restore Confirmation on Trash Double-Click).

## 🔒 My Identity
- Archetype: implementer
- Roles: implementer, qa
- Working directory: c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\teamwork_worker_m4_r5
- Original parent: 5194245e-d76b-4446-9341-e73f2b5a09dc
- Milestone: Milestone 4 (Requirement R5)

## 🔒 Key Constraints
- EXCLUSIVE WRITE OWNERSHIP:
  - MosquitoNetCalculator/Services/DialogService.cs
  - MosquitoNetCalculator/MainWindow.Orders.cs
  - MosquitoNetCalculator.Tests/Services/DialogBuilderTests.cs
- Integrity Mandate: Genuine implementation, no hardcoded verification strings or mocks bypassing logic.

## Current Parent
- Conversation ID: 5194245e-d76b-4446-9341-e73f2b5a09dc
- Updated: not yet

## Task Summary
- **What to build**:
  1. DialogService: Add `ShowRestoreConfirm` and `BuildRestoreConfirm` with message `$"Восстановить заказ «{contractNumber}» из корзины?"` and buttons `Восстановить` (PrimaryButton) / `Отмена` (GhostButton).
  2. MainWindow.Orders.cs: In `OpenSelectedOrder()`, handle trash item double click with interactive restore confirmation, dirty check, restore, refresh, and navigate to calculation.
  3. DialogBuilderTests.cs: Unit test `BuildRestoreConfirm_HasCorrectButtonsAndLabels`.
- **Success criteria**: dotnet build succeeds; DialogBuilderTests pass; trash order double click restores and loads cleanly.
- **Interface contracts**: PROJECT.md
- **Code layout**: MosquitoNetCalculator/

## Key Decisions Made
- TBD during investigation

## Artifact Index
- DISPATCH.md — task assignment
- BRIEFING.md — identity and state
- progress.md — heartbeat progress log
- handoff.md — final report

## Change Tracker
- **Files modified**: None
- **Build status**: Pending initial check
- **Pending issues**: None

## Quality Status
- **Build/test result**: Pending
- **Lint status**: Pending
- **Tests added/modified**: Pending

## Loaded Skills
- None
