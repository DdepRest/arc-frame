## 2026-11-02T12:50:08Z
You are teamwork_worker_m3.
Your working directory is: c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\teamwork_worker_m3_r4
Your parent is: 5194245e-d76b-4446-9341-e73f2b5a09dc

MANDATORY: Read the original user request before starting work:
c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\ORIGINAL_REQUEST.md

Read the project scope and explorer findings:
- PROJECT.md: c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\orchestrator\PROJECT.md
- Survey findings: c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\teamwork_explorer_s3\handoff.md
- Milestone 2 handoff: c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\teamwork_worker_m2\handoff.md

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

EXCLUSIVE WRITE OWNERSHIP (you may only edit these files):
- MosquitoNetCalculator/Models/OrderData.cs
- MosquitoNetCalculator/Services/OrderStorageService.cs
- MosquitoNetCalculator.Tests/Services/OrderStorageServiceTests.cs

Task: Implement Milestone 3 (Requirement R4: Trash Folder Cleanup and Empty Order Validation):
1. In `MosquitoNetCalculator/Models/OrderData.cs`:
   - Add helper property:
     ```csharp
     [System.Text.Json.Serialization.JsonIgnore]
     public bool IsEmptyDummy => (Items == null || Items.Count == 0) && Math.Abs(TotalAmount) < 0.001;
     ```
2. In `MosquitoNetCalculator/Services/OrderStorageService.cs`:
   - In `SoftDeleteOrder(string orderId)`:
     When deleting an order, check if it is an empty dummy order (`order.IsEmptyDummy`). If it is empty, delete it permanently from `OrdersDir` via `File.Delete(sourcePath)` and return immediately so it NEVER enters `TrashDir`.
   - Add public static method:
     ```csharp
     public static void CleanEmptyTrashOrders(string? trashDir = null)
     ```
     which iterates over `Directory.GetFiles(targetDir, "*.json")`, deserializes each file, and deletes any file where `order == null || order.IsEmptyDummy`.
   - Call `CleanEmptyTrashOrders()` in the `OrderStorageService` constructor and in `LoadTrashOrders()`.
   - Clean the production user trash folder: ensure existing empty dummy test order files (orders with 0 items and 0.00 ₽ sum) in `%APPDATA%\MosquitoNetCalculator\orders_trash` are purged.
3. In `MosquitoNetCalculator.Tests/Services/OrderStorageServiceTests.cs`:
   - Add unit tests:
     - `SoftDeleteOrder_EmptyDummyOrder_PermanentlyDeletesAndDoesNotEnterTrash`: verifies that soft-deleting an empty dummy order permanently deletes it from `OrdersDir` and leaves `TrashDir` empty.
     - `CleanEmptyTrashOrders_RemovesOnlyDummyOrders`: verifies that dummy orders are purged from trash while orders with valid items/amounts remain.
4. Build and verify:
   - `dotnet build MosquitoNetCalculator/MosquitoNetCalculator.csproj`
   - `dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --filter OrderStorageServiceTests`
   - Verify that `%APPDATA%\MosquitoNetCalculator\orders_trash` contains 0 empty dummy order files.

Write your complete handoff report to:
c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\teamwork_worker_m3_r4\handoff.md
Update progress.md in your directory as you work.
When finished, send a message to parent with summary and test results.
