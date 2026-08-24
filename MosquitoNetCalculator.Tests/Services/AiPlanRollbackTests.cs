using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.ViewModels;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Pins the atomic-plan behaviour behind the «Добавил, но в списке пусто» bug
    /// using the real <see cref="AiCommandExecutor"/>: in a batch plan the
    /// per-step «✅ Добавлено» toast is suppressed, and if a later step fails the
    /// executor rolls the WHOLE batch back — the earlier add must disappear.
    /// </summary>
    public class AiPlanRollbackTests
    {
        private static AiCommandExecutor CreateExecutor(
            CalculationViewModel calcVM,
            System.Action<string, ToastType> showToast,
            System.Action? pushUndo = null)
            => new AiCommandExecutor(
                calcVM,
                pushUndo: pushUndo ?? (() => { }),
                markDirty: () => { },
                recalculateAndUpdateTotal: () => { },
                showToast: showToast,
                isAiOverlayVisible: () => false,
                closeAiAssistant: () => { },
                openSlopeOverlay: (_, _, _, _) => { });

        private static AiCommand AddOtlivCommand() => new AiCommand
        {
            Type = AiCommandType.AddItem,
            Params = new AiCommandParams
            {
                Type = "Отлив",
                Color = "Белый",
                Width = 170,
                Height = 900,
                Quantity = 1,
                InstallationMode = 1
            }
        };

        [Fact]
        public void Execute_AddThenFailingStep_RollsBackAddedItem_AndShowsNoToast()
        {
            var calcVM = new CalculationViewModel();
            var snapshot = calcVM.SnapshotItems(); // empty, pre-batch state
            var toasts = new List<string>();
            var executor = CreateExecutor(calcVM, (msg, _) => toasts.Add(msg));

            // Step 1 is a real add through the real executor (batch mode:
            // pushUndo=false → no per-step toast); step 2 fails at runtime.
            var plan = AiPlanBuilder.FromCommands(new[]
            {
                AddOtlivCommand(),
                new AiCommand { Type = AiCommandType.DeleteLast }
            }, "отлив бел 170 900 без монтажа");

            int itemsAfterStep1 = -1;
            var result = AiPlanExecutor.Execute(plan, (AiCommand cmd, out string? err) =>
            {
                err = null;
                if (cmd.Type == AiCommandType.AddItem)
                {
                    var ok = executor.Execute(cmd, pushUndo: false, out err);
                    if (ok) itemsAfterStep1 = calcVM.OrderItems.Count;
                    return ok;
                }

                err = "сбой второго шага";
                return false;
            }, rollback: () => calcVM.RestoreFromSnapshot(snapshot, () => { }));

            Assert.False(result.Success);
            Assert.True(result.RolledBack);
            // The add really happened mid-batch…
            Assert.Equal(1, itemsAfterStep1);
            // …and the rollback removed it — the order is back to its pre-batch state.
            Assert.Empty(calcVM.OrderItems);
            // Batch mode must not have emitted the misleading per-step toast.
            Assert.Empty(toasts);
        }

        [Fact]
        public void Execute_SingleAction_ShowsAddToast()
        {
            var calcVM = new CalculationViewModel();
            var toasts = new List<string>();
            var executor = CreateExecutor(calcVM, (msg, _) => toasts.Add(msg));

            // Legacy single-action path (pushUndo=true) IS allowed to toast.
            var ok = executor.Execute(AddOtlivCommand(), pushUndo: true, out var error);

            Assert.True(ok);
            Assert.Null(error);
            Assert.Single(calcVM.OrderItems);
            Assert.Equal(1, calcVM.OrderItems[0].InstallationMode);
            Assert.Contains(toasts, t => t.Contains("✅ Добавлено"));
        }

        [Fact]
        public void Execute_DeleteLast_OnEmptyOrder_ReturnsErrorWithoutToast()
        {
            var calcVM = new CalculationViewModel();
            var toasts = new List<string>();
            var executor = CreateExecutor(calcVM, (msg, _) => toasts.Add(msg));

            var ok = executor.Execute(
                new AiCommand { Type = AiCommandType.DeleteLast },
                pushUndo: false,
                out var error);

            Assert.False(ok);
            Assert.Contains("нечего", error);
            Assert.Empty(toasts);
        }
    }
}
