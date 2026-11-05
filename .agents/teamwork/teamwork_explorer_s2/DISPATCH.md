## 2026-11-02T12:18:25Z
Task: Codebase Survey for Requirement R3 (Unit Test TrashDir Isolation).
1. Investigate OrderStorageService and related storage classes:
   - How are OrdersDir and TrashDir defined (static, instance, properties, default paths)?
   - Default path for TrashDir (`%APPDATA%\MosquitoNetCalculator\orders_trash`).
2. Search all unit and integration test files under MosquitoNetCalculator.Tests:
   - Identify every test class and test method that exercises OrderStorageService or orders/trash operations (e.g. OrdersHistoryViewModelTests, OrderStorageServiceTests, StabilityAndSafetyFeaturesTests, etc.).
   - Find which tests currently isolate `OrdersDir` vs `TrashDir`. Check if any test still points `TrashDir` to the production AppData folder.
   - Determine how tests can cleanly isolate BOTH `OrdersDir` and `TrashDir` to temporary directories (e.g., in test fixtures, setup/cleanup, or helper methods).
3. Verify what is needed so that running `dotnet test` creates ZERO files in `%APPDATA%\MosquitoNetCalculator\orders_trash`.
