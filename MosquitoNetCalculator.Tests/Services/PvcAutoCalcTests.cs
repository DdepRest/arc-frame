using System;
using System.Linq;
using System.Threading.Tasks;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.ViewModels;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class PvcAutoCalcTests
    {
        [Theory]
        [InlineData("Просчитай окно", true)]
        [InlineData("окно", true)]
        [InlineData("пвх", true)]
        [InlineData("чертёж окна", true)]
        [InlineData("эскиз замера", true)]
        [InlineData("расчёт ПВХ конструкции", true)]
        [InlineData("Сделай сетку 700х1400", false)]
        [InlineData("добавь ручку", false)]
        public void IsPvcWindowRequest_DetectsKeywordsAccurately(string text, bool expected)
        {
            bool actual = AiKeywordLexicon.IsPvcWindowRequest(text);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void IsPvcWindowRequest_DetectsNotepadNotations()
        {
            var ocrLines = new[] { "Димитрова 45/46", "П 400 х 1350", "О 130", "1060х1340" };
            Assert.True(AiKeywordLexicon.IsPvcWindowRequest(ocrLines));
        }

        [Fact]
        public void BuildPlanFromPvcResult_CreatesStructuredInteractivePlan()
        {
            var result = new PvcSketchResult
            {
                Success = true,
                Address = "Димитрова 45/46",
                OrderNumber = "135/2026-1105",
                TotalAmount = 12450,
                FrameWidth = 1060,
                FrameHeight = 1340,
                NetWidth = 490,
                NetHeight = 1260,
                DrainageWidth = 130,
                DrainageLength = 1200,
                SillWidth = 400,
                SillLength = 1350
            };

            var plan = PvcAutoCalcService.BuildPlanFromPvcResult(result, "Просчитай окно");

            Assert.NotNull(plan);
            Assert.Equal(AiPlanMode.Plan, plan.Mode);
            Assert.Equal(AiPlanStatus.AwaitingConfirmation, plan.Status);
            Assert.Contains("Димитрова 45/46", plan.Reasoning);
            Assert.Contains("1060 × 1340", plan.Reasoning);
            Assert.Contains(result.TotalAmount.ToString("N0"), plan.Reasoning);

            // Steps count: Address, Additional KP, Flashing, Mosquito Net
            Assert.Equal(4, plan.Steps.Count);

            // Step 1: Address
            var step0 = plan.Steps[0];
            Assert.Equal(AiCommandType.SetClientInfo, step0.CommandType);
            Assert.Equal("Димитрова 45/46", step0.Params.ClientAddress);

            // Step 2: Additional KP
            var step1 = plan.Steps[1];
            Assert.Equal(AiCommandType.AddAdditionalKp, step1.CommandType);
            Assert.Equal("135/2026-1105", step1.Params.AdditionalKpNumber);
            Assert.Equal(12450, step1.Params.AdditionalKpAmount);

            // Step 3: Flashing
            var step2 = plan.Steps[2];
            Assert.Equal(AiCommandType.AddItem, step2.CommandType);
            Assert.Equal("Отлив", step2.Params.Type);
            Assert.Equal(130, step2.Params.Width);
            Assert.Equal(1200, step2.Params.Height);

            // Step 4: Net
            var step3 = plan.Steps[3];
            Assert.Equal(AiCommandType.AddItem, step3.CommandType);
            Assert.Equal("Anwis", step3.Params.Type);
            Assert.Equal(490, step3.Params.Width);
            Assert.Equal(1260, step3.Params.Height);

            // Plan validation check
            var validation = AiPlanValidator.Validate(plan);
            Assert.True(validation.IsValid, string.Join("; ", validation.Messages));
        }

        [Fact]
        public void AiCommandExecutor_ExecutesAddAdditionalKp()
        {
            var calcVm = new CalculationViewModel();
            string addedKpNumber = "";
            double addedKpAmount = 0;

            var executor = new AiCommandExecutor(
                calcVm,
                pushUndo: () => { },
                markDirty: () => { },
                recalculateAndUpdateTotal: () => { },
                showToast: (m, t) => { },
                isAiOverlayVisible: () => false,
                closeAiAssistant: () => { },
                openSlopeOverlay: (w, h, d, c) => { },
                addAdditionalKp: (num, amt) =>
                {
                    addedKpNumber = num;
                    addedKpAmount = amt;
                });

            var cmd = new AiCommand
            {
                Type = AiCommandType.AddAdditionalKp,
                Params = new AiCommandParams
                {
                    AdditionalKpNumber = "TEST-123",
                    AdditionalKpAmount = 9990
                }
            };

            bool success = executor.Execute(cmd, pushUndo: false, out var error);

            Assert.True(success);
            Assert.Null(error);
            Assert.Equal("TEST-123", addedKpNumber);
            Assert.Equal(9990, addedKpAmount);
        }

        [Fact]
        public void ApplyToOrder_PutsAddressFlashingAndNetIntoOrder()
        {
            var result = new PvcSketchResult
            {
                Success = true,
                Address = "Демидова 45/46",
                OrderNumber = "2330",
                TotalAmount = 19286,
                FrameWidth = 1060,
                FrameHeight = 1340,
                NetWidth = 480,
                NetHeight = 1250,
                DrainageWidth = 130,
                DrainageLength = 1250,
                SillWidth = 400,
                SillLength = 1350
            };

            var calcVm = new CalculationViewModel();
            var client = new ClientInfo();
            bool recalculated = false;

            PvcAutoCalcService.ApplyToOrder(result, calcVm, client, () => recalculated = true);

            Assert.Equal("Демидова 45/46", client.ClientAddress);
            Assert.True(client.HasAdditionalKp);
            var kp = client.AdditionalKps.Last();
            Assert.Equal("2330", kp.Number);
            Assert.Equal(19286, kp.Amount);

            // ШиринаВвод/ВысотаВвод — размеры, с которыми строка добавлена в заказ
            // (Width/Height после этого пересчитывает AnwisSize).
            Assert.Contains(calcVm.OrderItems, i =>
                i.Name == "Отлив" && i.ШиринаВвод == 130 && i.ВысотаВвод == 1250);
            Assert.Contains(calcVm.OrderItems, i =>
                i.Name == "Anwis" && i.ШиринаВвод == 480 && i.ВысотаВвод == 1250);
            Assert.True(recalculated);
        }

        [Fact]
        public void LoadOrdersFromCcalcRegistry_BuildsOrderWithAddressNetAndFlashing()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ccalc_registry_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                var json = @"{
                  ""order_number"": ""2330"",
                  ""address"": ""Демидова 45/46"",
                  ""total_amount"": 19286.0,
                  ""frame"": { ""width_mm"": 1060, ""height_mm"": 1340 },
                  ""windowsill"": { ""width_mm"": 400, ""length_mm"": 1350 },
                  ""flashing"": { ""width_mm"": 130, ""length_mm"": 1250 },
                  ""mosquito_net"": { ""brand"": ""Anwis"", ""width_mm"": 480, ""height_mm"": 1250 }
                }";
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "2330.json"), json, System.Text.Encoding.UTF8);

                var orders = PvcAutoCalcService.LoadOrdersFromCcalcRegistry(dir);

                Assert.Single(orders);
                var order = orders[0];
                Assert.Equal("Демидова 45/46", order.ClientAddress);
                Assert.Equal("2330", order.AdditionalKpNumber);
                Assert.Equal(19286, order.TotalAmount);
                Assert.True(order.HasAdditionalKp);

                Assert.Contains(order.Items, i => i.Name == "Anwis" && i.Width == 480 && i.Height == 1250);

                // Размер сетки из CCalc не пересчитывается: режим «Профипласт» (иначе 445×1235 → 478×1280)
                Assert.Equal(
                    (int)MosquitoNetCalculator.Models.AnwisSizeMode.Профипласт,
                    order.Items.First(i => i.Name == "Anwis").AnwisSizeMode);
                Assert.Contains(order.Items, i => i.Name == "Отлив" && i.Width == 130 && i.Height == 1250);

                // Повторный импорт обновляет тот же заказ, а не плодит копии
                var again = PvcAutoCalcService.LoadOrdersFromCcalcRegistry(dir);
                Assert.Equal(order.Id, again[0].Id);
            }
            finally
            {
                try { System.IO.Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        [Fact]
        public async Task ProcessSketchAsync_FileNotFound_ReturnsError()
        {
            var res = await PvcAutoCalcService.ProcessSketchAsync("non_existent_file.jpg", dryRun: true);
            Assert.False(res.Success);
            Assert.Contains("не найден", res.Error);
        }
    }
}
