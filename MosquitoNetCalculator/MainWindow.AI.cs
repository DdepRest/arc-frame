using System;
using System.Windows;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.ViewModels;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator
{
    // Partial class for AI assistant integration.
    // Keeps the main MainWindow.xaml.cs clean.
    public partial class MainWindow
    {
        internal AiAssistantViewModel? AiVm { get; private set; }
        private Controls.AiAssistantWindow? _aiWindow;

        // Pre-batch snapshot + plan id of the last executed AI plan. The chat
        // «Отменить действие» button restores exactly this state (guarded so it
        // never wipes manual edits made after the AI action).
        private Models.OrderSnapshot? _lastAiPlanSnapshot;
        private string? _lastAiPlanId;

        /// <summary>
        /// Initializes the AI assistant and wires up command handling.
        /// Called from MainWindow constructor after OverlayManager is created.
        /// The AI toast scope is registered lazily on first AI open (not here)
        /// so the lifetime of the canvas mapping matches the lifetime of the
        /// opened AI surface exactly — no leaked references if AI is never used.
        /// </summary>
        private void InitAiAssistant()
        {
            AiVm = new AiAssistantViewModel();

            // Wire up command execution
            AiVm.CommandReceived += OnAiCommandReceived;
            AiVm.PlanReceived += OnAiPlanReceived;
            AiVm.UndoRequested += OnAiUndoRequested;
            AiVm.RedoRequested += OnAiRedoRequested;
            // Structured order context (totals match the UI) for the AI prompt
            // and for the local slash commands; raw items for «/объясни».
            AiVm.OrderContextProvider = () => AiOrderContextBuilder.Build(
                CalcVM.OrderItems,
                CalcVM.CalculateTotal(ClientInfo.AdditionalKpsTotal),
                ClientInfo.AdditionalKpsTotal);
            AiVm.OrderItemsProvider = () => CalcVM.OrderItems;

            // Bind the in-panel AI control to the shared ViewModel.
            AiAssistantControl.DataContext = AiVm;

            // Keep the AI assistant in sync with the main window.
            this.LocationChanged += OnMainWindowPositionChanged;
            this.SizeChanged += OnMainWindowPositionChanged;
            this.StateChanged += OnMainWindowStateChanged;

            // Reposition AI-anchored toasts if the panel's hosting canvas resizes
            // (in-panel resize comes through this SizeChanged already; the
            // docked window gets its own SizeChanged hooked in ShowDockedAiWindow).
            this.SizeChanged += (_, _) => ToastService.RepositionToasts(ToastService.AiScope);
        }

        /// <summary>
        /// Repositions the separate AI window next to the main window
        /// whenever the main window is moved or resized in windowed mode.
        /// </summary>
        private void OnMainWindowPositionChanged(object? sender, EventArgs e)
        {
            if (_aiWindow != null && _aiWindow.IsVisible && WindowState != WindowState.Minimized)
            {
                _aiWindow.PositionNextTo(this);
            }
        }

        /// <summary>
        /// Switches AI between in-panel and docked modes when the main window
        /// is maximized or restored while the AI is open.
        /// </summary>
        private void OnMainWindowStateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized) return;
            if (!IsAiOpen()) return;

            // Reopen AI in the mode that matches the new window state.
            RefreshAiMode();
        }

        /// <summary>
        /// Detaches event handlers and closes the AI assistant. Called when
        /// the main window is closing.
        /// </summary>
        private void DetachAiAssistant()
        {
            this.LocationChanged -= OnMainWindowPositionChanged;
            this.SizeChanged -= OnMainWindowPositionChanged;
            this.StateChanged -= OnMainWindowStateChanged;

            CloseAiAssistant();
        }

        /// <summary>
        /// Handler for the AI window being closed by the user. Clears the
        /// cached reference so the next toggle creates a fresh window and
        /// resets the active nav button.
        /// </summary>
        private void OnAiWindowClosed(object? sender, EventArgs e)
        {
            _aiWindow = null;
            SetActiveNavButton("Calc");
        }

        /// <summary>
        /// Determines whether the AI assistant is currently visible in either
        /// in-panel or docked mode.
        /// </summary>
        private bool IsAiOpen()
        {
            return AiOverlay.Visibility == Visibility.Visible
                   || (_aiWindow != null && _aiWindow.IsVisible);
        }

        /// <summary>
        /// Toggles the AI assistant on or off in the appropriate mode for the
        /// current main window state.
        /// </summary>
        internal void ToggleAiOverlay()
        {
            if (IsAiOpen())
            {
                CloseAiAssistant();
            }
            else
            {
                OpenAiAssistant();
            }
        }

        /// <summary>
        /// Shows the AI assistant in the mode that matches the current main
        /// window state: in-panel when maximized, docked otherwise. Registers
        /// the AI toast scope lazily on first open (paired with
        /// <see cref="CloseAiAssistant"/>’s unregister) so the canvas mapping
        /// lifetime exactly tracks the AI surface lifetime.
        /// </summary>
        private void OpenAiAssistant()
        {
            if (WindowState == WindowState.Maximized)
            {
                ToastService.RegisterCanvas(ToastService.AiScope, AiAssistantControl.ToastCanvas);
                AiOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                // First open of the dock: if the dock can't fit to the right,
                // center the WHOLE program. Mode refreshes (maximize/restore)
                // must NOT re-center — WPF restores the window position itself.
                ShowDockedAiWindow(centerProgramForDock: true);
            }

            SetActiveNavButton("AI");
        }

        /// <summary>
        /// Creates the docked AI window if needed, re-registers the "AI"
        /// toast scope to anchor toasts on the docked window instead of
        /// the in-panel canvas, hooks its SizeChanged for repositions, and
        /// shows it next to the main window.
        /// </summary>
        /// <param name="centerProgramForDock">
        /// True ONLY on first open (see <see cref="OpenAiAssistant"/>): when the
        /// dock can't fit to the right, the MAIN window is shifted so the whole
        /// group «program + dock» is centered on the screen. Mode refreshes
        /// (maximize → restore, <see cref="RefreshAiMode"/>) pass false so the
        /// main window keeps the position WPF restored — never re-centering it
        /// and never pinning it to the monitor edge.
        /// </param>
        private void ShowDockedAiWindow(bool centerProgramForDock = false)
        {
            EnsureDockedWindow();
            // Re-anchor AI toasts to the docked window's inner canvas so they
            // appear near the user's gaze, not in the main window corner.
            ToastService.RegisterCanvas(ToastService.AiScope, _aiWindow!.AiAssistantControl.ToastCanvas);
            // Reposition AI-anchored toasts when the docked window resizes
            // (its SizeChanged doesn’t bubble to MainWindow.SizeChanged).
            _aiWindow.SizeChanged -= OnAiWindowSizeChanged;
            _aiWindow.SizeChanged += OnAiWindowSizeChanged;
            if (centerProgramForDock)
            {
                CenterMainWindowForAiDock();
            }
            _aiWindow.PositionNextTo(this);
            _aiWindow.Show();
            _aiWindow.Activate();
        }

        /// <summary>Repositions AI-anchored toasts when the docked AI window is resized.</summary>
        private void OnAiWindowSizeChanged(object? sender, SizeChangedEventArgs e)
            => ToastService.RepositionToasts(ToastService.AiScope);

        /// <summary>
        /// Called when the docked AI window opens. If the dock can't fit flush to
        /// the RIGHT of the main window, shifts the MAIN window on BOTH axes so the
        /// whole group «program + dock» is centered on the screen — the user
        /// explicitly asked to center the whole program, not just the AI block.
        /// The dock follows (its top aligns with the program's top), so the entire
        /// program ends up truly centered. When the dock already fits, the user's
        /// manual placement of the main window is kept.
        /// </summary>
        private void CenterMainWindowForAiDock()
        {
            if (_aiWindow == null || WindowState != WindowState.Normal) return;

            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var handle = helper.Handle == IntPtr.Zero ? helper.EnsureHandle() : helper.Handle;
            var screen = System.Windows.Forms.Screen.FromHandle(handle);
            if (screen == null) return;

            var wa = screen.WorkingArea;
            double mainWidth = ActualWidth > 0 ? ActualWidth : Width;
            double mainHeight = ActualHeight > 0 ? ActualHeight : Height;
            double dockLeft = Left + mainWidth;
            if (dockLeft + _aiWindow.Width <= wa.Right) return; // fits — keep placement

            // Not enough room on the right — center the WHOLE program on both axes.
            Left = ComputeCenteredGroupLeft(wa.Left, wa.Right, mainWidth, _aiWindow.Width);
            Top = ComputeCenteredGroupTop(wa.Top, wa.Bottom, mainHeight);
        }

        /// <summary>
        /// Pure math (testable without a window handle): computes the main window's
        /// Left so the group «main window + AI dock» is horizontally centered on the
        /// working area, clamped so the main window itself stays fully on-screen.
        /// </summary>
        internal static double ComputeCenteredGroupLeft(
            double screenLeft, double screenRight,
            double mainWindowWidth, double aiWindowWidth)
        {
            double available = screenRight - screenLeft;
            double left = screenLeft + (available - (mainWindowWidth + aiWindowWidth)) / 2;
            return Math.Max(screenLeft, Math.Min(left, screenRight - mainWindowWidth));
        }

        /// <summary>
        /// Pure math (testable without a window handle): computes the main window's
        /// Top so the program is vertically centered on the working area (the dock
        /// aligns its top with the program's top, so the whole group stays centered).
        /// </summary>
        internal static double ComputeCenteredGroupTop(
            double screenTop, double screenBottom, double mainWindowHeight)
        {
            double available = screenBottom - screenTop;
            double top = screenTop + (available - mainWindowHeight) / 2;
            return Math.Max(screenTop, Math.Min(top, screenBottom - mainWindowHeight));
        }

        /// <summary>
        /// Creates and configures the docked AI window on demand.
        /// </summary>
        private void EnsureDockedWindow()
        {
            if (_aiWindow != null) return;

            _aiWindow = new Controls.AiAssistantWindow
            {
                Owner = this,
                ViewModel = AiVm
            };
            _aiWindow.Closed += OnAiWindowClosed;
        }

        /// <summary>
        /// Hides the AI assistant regardless of the current mode and
        /// unregisters the "AI" toast scope so lingering toasts get cleaned up.
        /// </summary>
        private void CloseAiAssistant()
        {
            AiOverlay.Visibility = Visibility.Collapsed;

            if (_aiWindow != null)
            {
                _aiWindow.Closed -= OnAiWindowClosed;
                _aiWindow.Close();
                _aiWindow = null;
            }

            // Drop the "AI" scope mapping entirely. Any in-flight AI toasts are
            // removed automatically by ToastService.UnregisterCanvas.
            ToastService.UnregisterCanvas(ToastService.AiScope);

            SetActiveNavButton("Calc");
        }

        /// <summary>
        /// Switches the AI assistant to the mode appropriate for the current
        /// main window state. Used when the main window state changes while
        /// the AI is already open. Re-registers the toast scope so new
        /// "Скопировано" toasts land on the now-visible canvas.
        ///
        /// Mode refreshes do NOT re-center the main window: the dock is shown
        /// with <c>centerProgramForDock: false</c> so restoring from maximized
        /// keeps the position WPF restores, instead of re-centering the program
        /// (which pinned it to the monitor edge on narrow screens).
        /// </summary>
        private void RefreshAiMode()
        {
            bool wasInPanel = AiOverlay.Visibility == Visibility.Visible;
            bool wasDocked = _aiWindow != null && _aiWindow.IsVisible;

            // Hide current mode.
            AiOverlay.Visibility = Visibility.Collapsed;
            if (_aiWindow != null)
            {
                _aiWindow.Hide();
            }

            // Drop the prior scope mapping before showing the new mode so
            // ShowDockedAiWindow / OpenAiAssistant re-registers cleanly.
            ToastService.UnregisterCanvas(ToastService.AiScope);

            // Show the mode matching the new state.
            if (WindowState == WindowState.Maximized)
            {
                ToastService.RegisterCanvas(ToastService.AiScope, AiAssistantControl.ToastCanvas);
                AiOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                ShowDockedAiWindow(); // no re-centering on maximize/restore
            }
        }

        /// <summary>
        /// Closes the in-panel AI overlay. Used by the overlay close button.
        /// </summary>
        private void CloseAiOverlay_Click(object sender, RoutedEventArgs e)
        {
            CloseAiAssistant();
        }

        /// <summary>
        /// Legacy single-action path (read-only commands and CalcSlope). Each
        /// command pushes its own undo snapshot, mirroring the pre-Agent-Mode
        /// behaviour.
        /// </summary>
        private void OnAiCommandReceived(AiCommand command)
        {
            Dispatcher.BeginInvoke(() =>
            {
                bool ok = ExecuteAiCommandCore(command, pushUndo: true, out var error);
                if (!ok)
                    ToastService.ShowToast(error ?? "Ошибка выполнения команды AI", ToastType.Error);
            });
        }

        /// <summary>
        /// Executes a confirmed plan atomically: ONE undo snapshot for the whole
        /// batch, rollback on any failure, and a result reported back to the
        /// chat bubble (enables «Отменить действие»).
        /// </summary>
        private void OnAiPlanReceived(AiActionPlan plan)
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var snapshot = ViewModel.SnapshotItems();
                    var result = AiPlanExecutor.Execute(
                        plan,
                        (AiCommand cmd, out string? err) => ExecuteAiCommandCore(cmd, pushUndo: false, out err),
                        rollback: () => RestoreSnapshot(snapshot));

                    if (result.Success)
                    {
                        // Single undo entry for the whole plan → one Ctrl+Z / chat
                        // undo reverts every step.
                        ViewModel.UndoRedo.PushUndo(snapshot);
                        _lastAiPlanSnapshot = snapshot;
                        _lastAiPlanId = plan.PlanId;
                        MarkDirty();
                        RecalculateAndUpdateTotal();
                        ToastService.ShowToast(result.Summary, ToastType.Success);
                    }
                    else
                    {
                        // Keep the atomicity promise even without an executor
                        // rollback (defensive): restore the pre-batch state.
                        if (!result.RolledBack)
                            RestoreSnapshot(snapshot);
                        _lastAiPlanSnapshot = null;
                        _lastAiPlanId = null;
                        ToastService.ShowToast(result.Error ?? "Ошибка выполнения", ToastType.Error);
                    }

                    AiVm?.OnPlanExecuted(plan.PlanId, result);
                    UpdateUndoRedoHint();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AI] Plan execution failed: {ex}");
                    ToastService.ShowToast($"Ошибка выполнения плана AI: {ex.Message}", ToastType.Error);
                }
            });
        }

        /// <summary>
        /// Executes one AI command against the real calculation. When
        /// <paramref name="pushUndo"/> is false (batch plan steps) the caller
        /// owns the single undo snapshot. Returns success + a user-facing error.
        /// </summary>
        private bool ExecuteAiCommandCore(AiCommand command, bool pushUndo, out string? error)
        {
            error = null;
            try
            {
                switch (command.Type)
                {
                    case AiCommandType.AddItem:
                    {
                        if (pushUndo) PushUndo();
                        var item = CalcVM.AddItem(
                            command.Params.Type,
                            command.Params.Color,
                            command.Params.Width,
                            command.Params.Height,
                            command.Params.Quantity,
                            command.Params.Price,
                            command.Params.AnwisMode);
                        if (item == null)
                        {
                            error = "Не удалось добавить позицию: неверные параметры.";
                            return false;
                        }

                        // Apply the installation mode the user asked for
                        // (0 = монтаж включён, 1 = без монтажа, 2 = в конструкцию).
                        // −1 means the user didn't mention it — the program's
                        // own default (from CalcVM.AddItem) is kept.
                        if (command.Params.InstallationMode >= 0)
                            item.InstallationMode = command.Params.InstallationMode;
                        item.RecalculateRequested += RecalculateAndUpdateTotal;
                        MarkDirty();
                        ToastService.ShowToast($"✅ Добавлено: {item.Name} {item.Color}", ToastType.Success);
                        RecalculateAndUpdateTotal();
                        return true;
                    }

                    case AiCommandType.DeleteLast:
                    {
                        if (CalcVM.OrderItems.Count == 0)
                        {
                            error = "Заказ пуст — удалять нечего.";
                            return false;
                        }
                        if (pushUndo) PushUndo();
                        var last = CalcVM.OrderItems[^1];
                        last.RecalculateRequested -= RecalculateAndUpdateTotal;
                        CalcVM.DeleteItem(last);
                        MarkDirty();
                        ToastService.ShowToast("🗑 Последняя позиция удалена", ToastType.Info);
                        RecalculateAndUpdateTotal();
                        return true;
                    }

                    case AiCommandType.ClearAll:
                    {
                        if (pushUndo) PushUndo();
                        CalcVM.UnsubscribeAll(RecalculateAndUpdateTotal);
                        CalcVM.ClearAll();
                        MarkDirty();
                        ToastService.ShowToast("🗑 Расчёт очищен", ToastType.Info);
                        RecalculateAndUpdateTotal();
                        return true;
                    }

                    case AiCommandType.ListProducts:
                        // The AI already listed products in its reply.
                        return true;

                    case AiCommandType.DeleteItems:
                    {
                        if (pushUndo) PushUndo();
                        int deleted = 0;
                        for (int i = CalcVM.OrderItems.Count - 1; i >= 0; i--)
                        {
                            var oi = CalcVM.OrderItems[i];
                            if (!MatchesTarget(oi, command.Params.TargetProduct)) continue;
                            oi.RecalculateRequested -= RecalculateAndUpdateTotal;
                            CalcVM.DeleteItem(oi);
                            deleted++;
                        }
                        MarkDirty();
                        RecalculateAndUpdateTotal();
                        ToastService.ShowToast($"🗑 Удалено позиций: {deleted}", ToastType.Info);
                        return true;
                    }

                    case AiCommandType.CalcSlope:
                    {
                        // Z-order guard (IN-PANEL mode only): AiOverlay is declared
                        // AFTER SlopeOverlay in MainWindow.xaml at the same
                        // Panel.ZIndex=15, so in maximized/in-panel mode it would
                        // render ON TOP of the freshly opened slope panel and hide
                        // it. Close the in-panel surface first (chat history
                        // persists in AiVm). In docked mode the AI is a SEPARATE
                        // window to the right of the program — it never overlaps
                        // the slope overlay, so the chat stays visible.
                        if (AiOverlay.Visibility == Visibility.Visible)
                            CloseAiAssistant();
                        ShowSlopeOverlay(
                            command.Params.Width,
                            command.Params.Height,
                            command.Params.Depth,
                            (int)Math.Max(1, command.Params.Quantity));
                        ToastService.ShowToast("🏗 Открыт просчёт откосов", ToastType.Info);
                        return true;
                    }

                    case AiCommandType.UpdateItems:
                    {
                        if (pushUndo) PushUndo();
                        int updatedCount = 0;
                        foreach (var oi in CalcVM.OrderItems)
                        {
                            if (!MatchesTarget(oi, command.Params.TargetProduct))
                                continue;
                            if (command.Params.UpdateInstallationMode.HasValue)
                                oi.InstallationMode = command.Params.UpdateInstallationMode.Value;
                            if (command.Params.UpdatePrice.HasValue)
                                oi.Price = command.Params.UpdatePrice.Value;
                            if (command.Params.UpdateAnwisMode.HasValue)
                                oi.AnwisSizeMode = command.Params.UpdateAnwisMode.Value;
                            if (command.Params.UpdateColor != null)
                                oi.Color = command.Params.UpdateColor;
                            if (command.Params.UpdateInstallationAmount.HasValue)
                                oi.SetCurrentInstallationAmount(command.Params.UpdateInstallationAmount.Value);
                            updatedCount++;
                        }
                        MarkDirty();
                        RecalculateAndUpdateTotal();
                        ToastService.ShowToast($"🔄 Обновлено позиций: {updatedCount}", ToastType.Success);
                        return true;
                    }

                    default:
                        error = "Неизвестная команда AI.";
                        return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AI] Command execution failed: {ex}");
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Restores a snapshot without touching the undo stack.</summary>
        private void RestoreSnapshot(Models.OrderSnapshot snapshot)
        {
            CalcVM.UnsubscribeAll(RecalculateAndUpdateTotal);
            ViewModel.RestoreFromSnapshot(snapshot, RecalculateAndUpdateTotal);
            UpdateTotal();
            UpdateEmptyState();
        }

        /// <summary>
        /// Chat «Отменить действие» / «/отменить»: undoes the LAST AI plan only.
        /// Guards against wiping manual edits made after the AI action — if the
        /// top undo snapshot is not the AI one, tells the user to use Ctrl+Z.
        /// </summary>
        private void OnAiUndoRequested()
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_lastAiPlanSnapshot == null)
                {
                    ToastService.ShowToast("Нечего отменять.", ToastType.Info);
                    return;
                }

                if (!ViewModel.UndoRedo.TryPeekTopSnapshot(out var top)
                    || top == null
                    || !SnapshotsEqual(top, _lastAiPlanSnapshot))
                {
                    ToastService.ShowToast(
                        "После действия были другие изменения. Безопасная отмена AI невозможна — используйте обычный Undo (Ctrl+Z).",
                        ToastType.Info);
                    var blockedId = _lastAiPlanId;
                    _lastAiPlanSnapshot = null;
                    _lastAiPlanId = null;
                    if (blockedId != null)
                        AiVm?.OnPlanUndoBlocked(blockedId);
                    return;
                }

                var prev = ViewModel.UndoRedo.Undo(ViewModel.SnapshotItems);
                if (prev != null)
                    RestoreFromSnapshot(prev);

                var planId = _lastAiPlanId;
                _lastAiPlanSnapshot = null;
                _lastAiPlanId = null;
                if (planId != null)
                    AiVm?.OnPlanUndone(planId);
                UpdateUndoRedoHint();
            });
        }

        private void OnAiRedoRequested()
        {
            Dispatcher.BeginInvoke(() =>
            {
                var next = ViewModel.UndoRedo.Redo(ViewModel.SnapshotItems);
                if (next != null)
                {
                    RestoreFromSnapshot(next);
                    UpdateUndoRedoHint();
                }
                else
                {
                    ToastService.ShowToast("Нечего повторять.", ToastType.Info);
                }
            });
        }

        private static bool SnapshotsEqual(Models.OrderSnapshot a, Models.OrderSnapshot b)
        {
            try
            {
                return System.Text.Json.JsonSerializer.Serialize(a)
                       == System.Text.Json.JsonSerializer.Serialize(b);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Handles the AI assistant nav button click.
        /// </summary>

        private void NavAi_Click(object sender, RoutedEventArgs e)
        {
            ToggleAiOverlay();
        }

        /// <summary>
        /// Matches an order item against a product/category filter for UpdateItems.
        /// </summary>
        private static bool MatchesTarget(OrderItem item, string target)
        {
            if (string.IsNullOrWhiteSpace(target) || target == "all")
                return true;
            var t = target.Trim().ToLowerInvariant();
            var name = item.Name;
            return t switch
            {
                "сетки" => name is "Anwis" or "На навесах" or "Оконная на метал. крепл." or "Дверная сетка",
                "фасадные" => name is "Отлив" or "Козырёк" or "Короб",
                "комплектующие" => name is "ПСУЛ" or "Уплотнение" or "Брус" or "Пояс" or "Материал",
                "услуги" => name is "Работа" or "Доставка",
                "откосы" => name is "Откос" or "Работа за откос",
                _ => string.Equals(name, target, StringComparison.OrdinalIgnoreCase)
            };
        }
    }
}
