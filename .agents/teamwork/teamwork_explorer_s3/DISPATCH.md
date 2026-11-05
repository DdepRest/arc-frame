## 2026-11-02T12:18:32Z
Task: Codebase Survey for Requirements R4 and R5.
1. Investigate R4 (Trash Folder Cleanup and Empty Order Validation):
   - Locate where orders are saved, moved to trash, deleted, or reset:
     * Check OrderStorageService (MoveToTrash, SaveOrder, etc.), OrdersHistoryViewModel (MoveToTrash, Delete, etc.), CalculationViewModel, ActionBarControl, etc.
     * How are orders structured (e.g. OrderRecord, OrderItems, ItemCount, TotalPrice, Sum)?
     * What defines an "empty dummy order" (orders with 0 items and 0.00 ₽ sum)?
     * How and where to validate/filter so empty dummy orders cannot be saved or moved into trash upon save or reset.
     * How to clean the production user trash folder (`%APPDATA%\MosquitoNetCalculator\orders_trash`) of existing empty dummy test order files (0 items and 0.00 ₽ sum) — where should this cleanup run (e.g., during OrderStorageService initialization, app startup, or helper)?
2. Investigate R5 (Interactive Restore Confirmation on Trash Double-Click):
   - Locate OrdersHistoryControl.xaml, OrdersHistoryControl.xaml.cs, and OrdersHistoryViewModel.cs.
   - Find how double-clicking an item in the trash list currently behaves:
     * Does it trigger a command, an event, or a toast notification warning?
     * How should it instead immediately display a confirmation dialog: «Восстановить заказ «{номер}» из корзины?» with [ Восстановить ] and [ Отмена ] buttons?
     * Check existing dialog infrastructure (e.g. IDialogService, MessageBox, custom WPF confirmation dialog, Metro/Modern dialogs if any).
     * Upon confirmation: how is the order restored from trash (MoveFromTrash / RestoreFromTrash) and opened in calculation view (e.g., navigating to CalculationView, loading the order into CalculationViewModel)?
     * Upon cancellation: ensure no toast spam, leaving the order in trash.
3. Check existing tests for trash operations, order restoration, and dialogs.

Write your complete, structured analysis and findings to:
c:\Users\Asus\Desktop\A.R.C. Frame\gwga\.agents\teamwork\teamwork_explorer_s3\handoff.md
Update progress.md in your directory as you work.
When finished, send a message to parent with a concise summary and reference to handoff.md.
