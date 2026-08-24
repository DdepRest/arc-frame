using System;
using System.Collections.Generic;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.ViewModels;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Behavioural coverage for <see cref="AiCommandExecutor"/> that previously
    /// lived as source-scan tests in AppLifecycleTests: the calc_slope Z-order
    /// guard and the installation-mode application must be verified by running
    /// the real executor with injected hooks, not by grepping the .cs file.
    /// </summary>
    public class AiCommandExecutorTests
    {
        private static AiCommandExecutor CreateExecutor(
            CalculationViewModel calcVM,
            Action<string, ToastType>? showToast = null,
            Func<bool>? isAiOverlayVisible = null,
            Action? closeAiAssistant = null,
            Action<int, int, int, int>? openSlopeOverlay = null)
            => new AiCommandExecutor(
                calcVM,
                pushUndo: () => { },
                markDirty: () => { },
                recalculateAndUpdateTotal: () => { },
                showToast: showToast ?? ((_, _) => { }),
                isAiOverlayVisible: isAiOverlayVisible ?? (() => false),
                closeAiAssistant: closeAiAssistant ?? (() => { }),
                openSlopeOverlay: openSlopeOverlay ?? ((_, _, _, _) => { }));

        private static AiCommand AddOtlivCommand(int installationMode = 1) => new AiCommand
        {
            Type = AiCommandType.AddItem,
            Params = new AiCommandParams
            {
                Type = "Отлив",
                Color = "Белый",
                Width = 170,
                Height = 900,
                Quantity = 1,
                InstallationMode = installationMode
            }
        };

        private static AiCommand CalcSlopeCommand(int width, int height, int depth, int quantity)
            => new AiCommand
            {
                Type = AiCommandType.CalcSlope,
                Params = new AiCommandParams
                {
                    Width = width,
                    Height = height,
                    Depth = depth,
                    Quantity = quantity
                }
            };

        // --- installation mode -------------------------------------------------

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void Execute_AddItem_AppliesRequestedInstallationMode(int mode)
        {
            var calcVM = new CalculationViewModel();
            var executor = CreateExecutor(calcVM);

            var ok = executor.Execute(AddOtlivCommand(mode), pushUndo: true, out var error);

            Assert.True(ok);
            Assert.Null(error);
            Assert.Single(calcVM.OrderItems);
            Assert.Equal(mode, calcVM.OrderItems[0].InstallationMode);
        }

        [Fact]
        public void Execute_AddItem_UnspecifiedInstallationMode_KeepsProgramDefault()
        {
            var calcVM = new CalculationViewModel();
            var executor = CreateExecutor(calcVM);

            // -1 = the user never mentioned a mode → keep the program's own
            // default (CalcVM.AddItem), which for per-linear-meter products
            // like Отлив is 1 (без монтажа).
            var reference = new CalculationViewModel();
            var expected = reference.AddItem("Отлив", "Белый", 170, 900, 1, 0)!.InstallationMode;

            var ok = executor.Execute(AddOtlivCommand(installationMode: -1), pushUndo: true, out var error);

            Assert.True(ok);
            Assert.Null(error);
            Assert.Single(calcVM.OrderItems);
            Assert.Equal(expected, calcVM.OrderItems[0].InstallationMode);
        }

        // --- calc_slope ---------------------------------------------------------

        [Fact]
        public void Execute_CalcSlope_OpensOverlayWithRequestedDimensions_AndToasts()
        {
            var calcVM = new CalculationViewModel();
            var toasts = new List<string>();
            var opens = new List<(int w, int h, int d, int q)>();
            var closed = 0;
            var executor = CreateExecutor(
                calcVM,
                showToast: (msg, _) => toasts.Add(msg),
                isAiOverlayVisible: () => false,
                closeAiAssistant: () => closed++,
                openSlopeOverlay: (w, h, d, q) => opens.Add((w, h, d, q)));

            var ok = executor.Execute(CalcSlopeCommand(170, 900, 100, 2), pushUndo: true, out var error);

            Assert.True(ok);
            Assert.Null(error);
            var (w, h, d, q) = Assert.Single(opens);
            Assert.Equal(170, w);
            Assert.Equal(900, h);
            Assert.Equal(100, d);
            Assert.Equal(2, q);
            // In-panel AI overlay is not visible → the chat must stay open.
            Assert.Equal(0, closed);
            Assert.Contains(toasts, t => t.Contains("🏗 Открыт просчёт откосов"));
        }

        [Fact]
        public void Execute_CalcSlope_ClosesAiOverlay_BeforeOpeningSlope()
        {
            var calcVM = new CalculationViewModel();
            var events = new List<string>();
            var executor = CreateExecutor(
                calcVM,
                isAiOverlayVisible: () => true,
                closeAiAssistant: () => events.Add("close"),
                openSlopeOverlay: (_, _, _, _) => events.Add("open"));

            var ok = executor.Execute(CalcSlopeCommand(1, 1, 1, 1), pushUndo: true, out var error);

            Assert.True(ok);
            Assert.Null(error);
            // The freshly opened slope panel must never be hidden behind the
            // in-panel AI overlay: close FIRST, then open.
            Assert.Equal(new[] { "close", "open" }, events);
        }

        [Fact]
        public void Execute_CalcSlope_ClampsWindowCountToAtLeastOne()
        {
            var calcVM = new CalculationViewModel();
            var opens = new List<(int w, int h, int d, int q)>();
            var executor = CreateExecutor(
                calcVM,
                openSlopeOverlay: (w, h, d, q) => opens.Add((w, h, d, q)));

            var ok = executor.Execute(CalcSlopeCommand(170, 900, 100, 0), pushUndo: true, out var error);

            Assert.True(ok);
            Assert.Null(error);
            var (_, _, _, q) = Assert.Single(opens);
            Assert.Equal(1, q);
        }
    }
}
