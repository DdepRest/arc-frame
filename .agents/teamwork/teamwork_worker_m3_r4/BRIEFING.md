# BRIEFING — 2026-11-02T12:51:00Z

## Mission
Implement Milestone 3 (Requirement R4: Trash Folder Cleanup and Empty Order Validation) for MosquitoNetCalculator.

## 🔒 My Identity
- Archetype: worker
- Roles: implementer, qa
- Working directory: c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\teamwork_worker_m3_r4
- Original parent: 5194245e-d76b-4446-9341-e73f2b5a09dc
- Milestone: Milestone 3 (R4: Trash Folder Cleanup and Empty Order Validation)

## 🔒 Key Constraints
- EXCLUSIVE WRITE OWNERSHIP:
  - MosquitoNetCalculator/Models/OrderData.cs
  - MosquitoNetCalculator/Services/OrderStorageService.cs
  - MosquitoNetCalculator.Tests/Services/OrderStorageServiceTests.cs
  - .agents/teamwork/teamwork_worker_m3_r4/*
- Integrity Mandate: genuine logic only, no cheating/hardcoding/facades.
- Verify production trash folder clean-up (%APPDATA%\MosquitoNetCalculator\orders_trash).

## Current Parent
- Conversation ID: 5194245e-d76b-4446-9341-e73f2b5a09dc
- Updated: 2026-11-02T12:51:00Z

## Task Summary
- **What to build**:
  - Add `IsEmptyDummy` property to `OrderData.cs`.
  - Update `SoftDeleteOrder` in `OrderStorageService.cs` to permanently delete empty dummy orders instead of soft-deleting to trash.
  - Add `CleanEmptyTrashOrders` static method in `OrderStorageService.cs` and invoke it in constructor and `LoadTrashOrders()`.
  - Purge existing empty dummy test orders from `%APPDATA%\MosquitoNetCalculator\orders_trash`.
  - Add unit tests in `OrderStorageServiceTests.cs`.
- **Success criteria**:
  - `dotnet build MosquitoNetCalculator/MosquitoNetCalculator.csproj` passes.
  - `dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --filter OrderStorageServiceTests` passes.
  - `%APPDATA%\MosquitoNetCalculator\orders_trash` contains 0 dummy order files.
- **Interface contracts**: PROJECT.md / ORIGINAL_REQUEST.md
- **Code layout**: Existing project structure

## Key Decisions Made
- [TBD]

## Artifact Index
- DISPATCH.md — assignment dispatch
- BRIEFING.md — working memory
- progress.md — liveness tracker
- handoff.md — final 5-component handoff report

## Change Tracker
- **Files modified**: None yet
- **Build status**: Not run yet
- **Pending issues**: None

## Quality Status
- **Build/test result**: Not run yet
- **Lint status**: clean
- **Tests added/modified**: TBD

## Loaded Skills
None
