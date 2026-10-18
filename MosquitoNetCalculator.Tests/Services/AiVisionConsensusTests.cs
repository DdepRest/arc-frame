using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class AiVisionConsensusTests
    {
        [Fact]
        public void ParseDrawingItems_IdentifiesGlassPackageAndNetsCorrectly()
        {
            const string json = @"{
  ""items"": [
    { ""type"": ""Anwis"", ""width"": 414, ""height"": 1143, ""is_net"": true, ""symbol"": ""#"", ""notes"": ""сетка 1"" },
    { ""type"": ""Anwis"", ""width"": 531, ""height"": 1162, ""is_net"": true, ""symbol"": ""#"", ""notes"": ""сетка 2"" },
    { ""type"": ""Anwis"", ""width"": 532, ""height"": 1143, ""is_net"": true, ""symbol"": ""#"", ""notes"": ""сетка 3"" },
    { ""type"": ""СП"", ""width"": 348, ""height"": 1076, ""is_net"": false, ""symbol"": ""СП"", ""notes"": ""2ст 24 мм"" }
  ]
}";

            var items = AiVisionConsensusService.ParseDrawingItems(json);

            Assert.Equal(4, items.Count);

            var nets = items.Where(i => i.IsNet).ToList();
            Assert.Equal(3, nets.Count);
            Assert.Contains(nets, n => n.Width == 414 && n.Height == 1143);
            Assert.Contains(nets, n => n.Width == 531 && n.Height == 1162);
            Assert.Contains(nets, n => n.Width == 532 && n.Height == 1143);

            var glass = items.FirstOrDefault(i => !i.IsNet);
            Assert.NotNull(glass);
            Assert.Equal("Стеклопакет", glass.Type);
            Assert.Equal(348, glass.Width);
            Assert.Equal(1076, glass.Height);
        }

        [Fact]
        public void ParseDrawingItems_EnforcesSpRuleEvenIfModelLabeledItNet()
        {
            // Even if a model mistakenly set is_net = true on an item named «СП»:
            const string json = @"{
  ""items"": [
    { ""type"": ""СП"", ""width"": 348, ""height"": 1076, ""is_net"": true, ""symbol"": ""СП"" }
  ]
}";

            var items = AiVisionConsensusService.ParseDrawingItems(json);

            Assert.Single(items);
            Assert.False(items[0].IsNet, "СП must never be accepted as a net");
            Assert.Equal("Стеклопакет", items[0].Type);
        }

        [Fact]
        public void ReconcileItems_WhenBothModelsAgree_ReturnsConsensusTrue()
        {
            var model1 = new List<AiDrawingItem>
            {
                new("Anwis", 414, 1143, true, "#"),
                new("Anwis", 531, 1162, true, "#"),
                new("Anwis", 532, 1143, true, "#")
            };

            var model2 = new List<AiDrawingItem>
            {
                new("Anwis", 414, 1143, true, "#"),
                new("Anwis", 531, 1162, true, "#"),
                new("Anwis", 532, 1143, true, "#")
            };

            var result = AiVisionConsensusService.ReconcileItems(model1, model2, null, "3 сетки");

            Assert.True(result.IsConsensus);
            Assert.Equal(2, result.ModelsComparedCount);
            Assert.Equal(3, result.Items.Count);
            Assert.NotNull(result.GeneratedPlan);
            Assert.Equal(3, result.GeneratedPlan!.Steps.Count);
        }

        [Fact]
        public void ReconcileItems_WhenModelsDisagree_ArbitratesUsingLocalOcr()
        {
            // Model 1 read 500x1100, Model 2 read 531x1162
            var model1 = new List<AiDrawingItem>
            {
                new("Anwis", 500, 1100, true, "#")
            };

            var model2 = new List<AiDrawingItem>
            {
                new("Anwis", 531, 1162, true, "#")
            };

            // Local OCR saw 531 and 1162 in the image pixels
            var ocrLines = new[] { "531", "1162" };

            var result = AiVisionConsensusService.ReconcileItems(model1, model2, ocrLines, "сетка");

            Assert.False(result.IsConsensus);
            Assert.True(result.HasDiscrepancy);
            Assert.NotNull(result.DiscrepancyExplanation);
            Assert.Single(result.Items);
            Assert.Equal(531, result.Items[0].Width);
            Assert.Equal(1162, result.Items[0].Height);
        }

        [Fact]
        public void CleanDisplayText_SuppressesLeakedEnglishChainOfThought()
        {
            const string leakedCoT =
@"The user wants to calculate an ""Anwis"" window (проём) with installation (монтаж) based on a hand-drawn sketch.
1. Analyze the image:
- It's a hand-drawn sketch on graph paper.
- Inside the rectangle, there is a hash symbol (#).
Below the rectangle, there is a number ""607"".
Height: 1278.
Width: 607.";

            var cleaned = AiChatMessage.CleanDisplayText(leakedCoT);

            Assert.Empty(cleaned);
        }

        [Fact]
        public void CleanDisplayText_PreservesRussianContentAfterEnglishScratchpad()
        {
            const string mixed =
@"The user wants to calculate...
1. Analyze the image...
Я распознал чертёж и подготовил расчёт:";

            var cleaned = AiChatMessage.CleanDisplayText(mixed);

            Assert.StartsWith("Я распознал", cleaned);
        }

        [Fact]
        public void TryParse_WithItemsJson_CreatesMultiItemPlan()
        {
            const string json = @"{
  ""items"": [
    { ""type"": ""Anwis"", ""width"": 414, ""height"": 1143, ""is_net"": true },
    { ""type"": ""Anwis"", ""width"": 531, ""height"": 1162, ""is_net"": true },
    { ""type"": ""Anwis"", ""width"": 532, ""height"": 1143, ""is_net"": true },
    { ""type"": ""СП"", ""width"": 348, ""height"": 1076, ""is_net"": false }
  ]
}";

            var (response, isValid) = AiCommandParser.TryParse(json, "проём с монтажом anwis");

            Assert.True(isValid);
            Assert.NotNull(response.Plan);
            Assert.Equal(3, response.Plan!.Steps.Count);
            Assert.Equal(414, response.Plan.Steps[0].Params.Width);
            Assert.Equal(1143, response.Plan.Steps[0].Params.Height);
            Assert.Equal(531, response.Plan.Steps[1].Params.Width);
            Assert.Equal(1162, response.Plan.Steps[1].Params.Height);
            Assert.Equal(532, response.Plan.Steps[2].Params.Width);
            Assert.Equal(1143, response.Plan.Steps[2].Params.Height);
        }

        [Fact]
        public void TryExtractCommandFromProse_ExtractsMultipleNetsAndSkipsSp()
        {
            const string prose =
@"The user wants to calculate sketch:
Top has СП: 348x1076.
Bottom has 3 nets:
Net 1: 414x1143
Net 2: 531x1162
Net 3: 532x1143";

            var (response, isValid) = AiCommandParser.TryParse(prose, "проём с монтажом anwis");

            Assert.True(isValid);
            Assert.NotNull(response.Plan);
            Assert.Equal(3, response.Plan!.Steps.Count);
            Assert.Equal(414, response.Plan.Steps[0].Params.Width);
            Assert.Equal(531, response.Plan.Steps[1].Params.Width);
            Assert.Equal(532, response.Plan.Steps[2].Params.Width);
        }

        [Fact]
        public void SanitizeDrawingPlan_ReconcilesTwinSashesAndCorrects1242To1272()
        {
            // Scenario from user's bug report:
            // Middle net was read as 608x724 (top-to-bottom truncated reading of vertical 1272)
            // Right net was read as 608x1242 (7 vs 4 misread of 1272)
            var plan = new AiActionPlan();
            plan.Steps.Add(new AiActionStep
            {
                CommandType = AiCommandType.AddItem,
                Params = new AiCommandParams { Type = "Anwis", Width = 608, Height = 724, Quantity = 1 }
            });
            plan.Steps.Add(new AiActionStep
            {
                CommandType = AiCommandType.AddItem,
                Params = new AiCommandParams { Type = "Anwis", Width = 608, Height = 1242, Quantity = 1 }
            });

            MosquitoNetCalculator.ViewModels.AiAssistantViewModel.SanitizeDrawingPlan(plan);

            Assert.Equal(2, plan.Steps.Count);
            // Both nets reconciled to identical twin window sash dimensions: 608 x 1272
            Assert.Equal(608, plan.Steps[0].Params.Width);
            Assert.Equal(1272, plan.Steps[0].Params.Height);

            Assert.Equal(608, plan.Steps[1].Params.Width);
            Assert.Equal(1272, plan.Steps[1].Params.Height);
        }

        [Fact]
        public void SanitizeDrawingPlan_ConvertsSpStepsToCustomProduct()
        {
            var plan = new AiActionPlan();
            plan.Steps.Add(new AiActionStep
            {
                CommandType = AiCommandType.AddItem,
                Params = new AiCommandParams { Type = "СП", Width = 615, Height = 1303, Quantity = 1 }
            });
            plan.Steps.Add(new AiActionStep
            {
                CommandType = AiCommandType.AddItem,
                Params = new AiCommandParams { Type = "Anwis", Width = 608, Height = 1272, Quantity = 2 }
            });

            MosquitoNetCalculator.ViewModels.AiAssistantViewModel.SanitizeDrawingPlan(plan);

            Assert.Equal(2, plan.Steps.Count);
            // СП converted to custom product "Стеклопакет"
            Assert.Equal("Стеклопакет", plan.Steps[0].Params.Type);
            Assert.True(plan.Steps[0].Params.IsCustomProduct);

            // Anwis untouched
            Assert.Equal("Anwis", plan.Steps[1].Params.Type);
            Assert.Equal(608, plan.Steps[1].Params.Width);
            Assert.Equal(1272, plan.Steps[1].Params.Height);
        }

        [Fact]
        public void PickCandidateVisionModels_ExcludesOpenRouterFreeAndTextOnlyModels()
        {
            var candidates = AiVisionConsensusService.PickCandidateVisionModels(AiAssistantService.OpenRouterFreeRouter);

            Assert.DoesNotContain(AiAssistantService.OpenRouterFreeRouter, candidates);
            Assert.NotEmpty(candidates);
            // All candidates must have vision capability
            foreach (var c in candidates)
            {
                Assert.True(AiAssistantService.FreeModels.Any(m => m.Id == c && m.SupportsVision == true) || c.Contains("vision") || c.Contains("-vl"));
            }
        }
    }
}
