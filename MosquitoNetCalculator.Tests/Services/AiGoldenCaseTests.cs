using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Offline golden regression suite: real manager-style phrases with recorded
    /// model responses. Guards parser, parameter normalization and the local
    /// confirmation policy — the LLM is never called.
    /// </summary>
    public class AiGoldenCaseTests
    {
        public sealed class GoldenCase
        {
            [JsonPropertyName("id")] public string Id { get; set; } = "";
            [JsonPropertyName("user_text")] public string UserText { get; set; } = "";
            [JsonPropertyName("model_response")] public string ModelResponse { get; set; } = "";
            [JsonPropertyName("expected_action")] public string? ExpectedAction { get; set; }
            [JsonPropertyName("expected_mode")] public string? ExpectedMode { get; set; }
            [JsonPropertyName("expected_type")] public string? ExpectedType { get; set; }
            [JsonPropertyName("expected_color")] public string? ExpectedColor { get; set; }
            [JsonPropertyName("expected_width")] public int? ExpectedWidth { get; set; }
            [JsonPropertyName("expected_height")] public int? ExpectedHeight { get; set; }
            [JsonPropertyName("expected_depth")] public int? ExpectedDepth { get; set; }
            [JsonPropertyName("expected_quantity")] public double? ExpectedQuantity { get; set; }
            [JsonPropertyName("expected_price")] public double? ExpectedPrice { get; set; }
            [JsonPropertyName("expected_installation_mode")] public int? ExpectedInstallationMode { get; set; }
            [JsonPropertyName("expected_update_installation_mode")] public int? ExpectedUpdateInstallationMode { get; set; }
            [JsonPropertyName("expected_update_price")] public double? ExpectedUpdatePrice { get; set; }
            [JsonPropertyName("expected_anwis_mode")] public string? ExpectedAnwisMode { get; set; }
            [JsonPropertyName("expected_target")] public string? ExpectedTarget { get; set; }
            [JsonPropertyName("expected_steps")] public int? ExpectedSteps { get; set; }
            [JsonPropertyName("expected_confirmation")] public bool ExpectedConfirmation { get; set; }
            /// <summary>
            /// When set, locks the interception decision («execute vs clarify»)
            /// via <see cref="AiClarificationForm.ShouldAskForMissingParams"/> on the
            /// parsed commands + user text. Covers the монтаж / Anwis-mode /
            /// dimensions guards that live above the plain plan validation.
            /// </summary>
            [JsonPropertyName("expected_clarification")] public bool? ExpectedClarification { get; set; }
        }

        private static IReadOnlyList<GoldenCase> Load()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "AI", "golden-cases.json");
            Assert.True(File.Exists(path), $"golden-cases.json not found at {path}");
            return JsonSerializer.Deserialize<List<GoldenCase>>(File.ReadAllText(path))!;
        }

        [Fact]
        public void AllGoldenCases_AreValidJson_AndNonEmpty()
        {
            var cases = Load();
            Assert.NotEmpty(cases);
            Assert.All(cases, c => Assert.False(string.IsNullOrWhiteSpace(c.Id)));
        }

        [Theory]
        [MemberData(nameof(AllCases))]
        public void GoldenCase_Offline_MatchesExpectations(GoldenCase c)
        {
            var (response, isValid) = AiCommandParser.TryParse(c.ModelResponse, c.UserText);
            Assert.True(isValid, $"[{c.Id}] ответ модели должен парситься");

            if (c.ExpectedMode == "clarification")
            {
                Assert.Null(response.Action);
                Assert.Null(response.Plan);
                Assert.True(AiClarificationForm.LooksLikeClarification(c.ModelResponse),
                    $"[{c.Id}] ответ должен распознаваться как уточнение");
                return;
            }

            // Normalize to a plan (either the model sent plan-mode, or we wrap the single action).
            AiActionPlan plan;
            if (response.Plan != null)
            {
                plan = response.Plan;
                if (c.ExpectedSteps.HasValue)
                    Assert.Equal(c.ExpectedSteps.Value, plan.Steps.Count);
            }
            else
            {
                Assert.NotNull(response.Action);
                plan = AiPlanBuilder.FromCommand(response.Action!, c.UserText, response.Reply);
            }

            var validation = AiPlanValidator.Validate(plan);
            Assert.True(validation.IsValid, $"[{c.Id}] план должен проходить локальную валидацию: {string.Join("; ", validation.StepResults.SelectMany(r => r.Messages))}");
            Assert.Equal(c.ExpectedConfirmation, plan.RequiresConfirmation);

            // Interception layer: lock the «execute vs clarify» decision so a plan
            // can't silently run with missing монтаж / Anwis mode / dimensions.
            // The «don't invent» policy now lives in <see cref="AiPlanSafetyPolicy"/>;
            // the public AiClarificationForm.ShouldAskForMissingParams is the
            // form-side helper restricted to AddItem, while safety policy is the
            // full superset that also covers untargeted updates.
            if (c.ExpectedClarification.HasValue)
            {
                var commands = plan.Steps.Select(s => s.ToCommand()).ToArray();
                bool policyBlocks = AiPlanSafetyPolicy.NeedsClarification(commands, c.UserText);
                Assert.Equal(c.ExpectedClarification.Value, policyBlocks);
            }

            var first = plan.Steps[0];
            if (c.ExpectedAction != null)
            {
                Assert.Equal(c.ExpectedAction, first.CommandType.ToString().ToLowerInvariant() switch
                {
                    "additem" => "add_item",
                    "deletelast" => "delete_last",
                    "deleteitems" => "delete_items",
                    "clearall" => "clear_all",
                    "listproducts" => "list_products",
                    "calcslope" => "calc_slope",
                    "updateitems" => "update_items",
                    _ => first.CommandType.ToString().ToLowerInvariant()
                });
            }

            if (c.ExpectedType != null) Assert.Equal(c.ExpectedType, first.Params.Type);
            if (c.ExpectedColor != null) Assert.Equal(c.ExpectedColor, first.Params.Color);
            if (c.ExpectedWidth.HasValue) Assert.Equal(c.ExpectedWidth.Value, first.Params.Width);
            if (c.ExpectedHeight.HasValue) Assert.Equal(c.ExpectedHeight.Value, first.Params.Height);
            if (c.ExpectedDepth.HasValue) Assert.Equal(c.ExpectedDepth.Value, first.Params.Depth);
            if (c.ExpectedQuantity.HasValue) Assert.Equal(c.ExpectedQuantity.Value, first.Params.Quantity);
            if (c.ExpectedPrice.HasValue) Assert.Equal(c.ExpectedPrice.Value, first.Params.Price);
            if (c.ExpectedInstallationMode.HasValue) Assert.Equal(c.ExpectedInstallationMode.Value, first.Params.InstallationMode);
            if (c.ExpectedUpdateInstallationMode.HasValue) Assert.Equal(c.ExpectedUpdateInstallationMode.Value, first.Params.UpdateInstallationMode);
            if (c.ExpectedUpdatePrice.HasValue) Assert.Equal(c.ExpectedUpdatePrice.Value, first.Params.UpdatePrice);
            if (c.ExpectedTarget != null) Assert.Equal(c.ExpectedTarget, first.Params.TargetProduct);
            if (c.ExpectedAnwisMode != null)
                Assert.Equal(c.ExpectedAnwisMode, AiCommandParser.AnwisModeLabel(first.Params.AnwisMode));
        }

        public static IEnumerable<object[]> AllCases()
            => Load().Select(c => new object[] { c });
    }
}
