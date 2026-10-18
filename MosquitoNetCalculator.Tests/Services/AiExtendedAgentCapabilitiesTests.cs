using System;
using System.Collections.Generic;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.ViewModels;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class AiExtendedAgentCapabilitiesTests
    {
        [Fact]
        public void ParseAddItem_WithAnticat_ParsesFlag()
        {
            var json = """
                ```json
                {
                  "action": "add_item",
                  "params": {
                    "type": "Anwis",
                    "color": "Белый",
                    "width": 600,
                    "height": 1300,
                    "anwis_mode": "ББ60",
                    "anticat": true
                  }
                }
                ```
                """;

            var (resp, valid) = AiCommandParser.TryParse(json, "сетка с антикошкой");
            Assert.True(valid);
            Assert.NotNull(resp.Action);
            Assert.Equal(AiCommandType.AddItem, resp.Action!.Type);
            Assert.True(resp.Action.Params.IsAnticat);
        }

        [Fact]
        public void ParseUpdateItemByIndex_ParsesFields()
        {
            var json = """
                ```json
                {
                  "action": "update_item_by_index",
                  "params": {
                    "index": 2,
                    "width": 750,
                    "height": 1450,
                    "quantity": 3,
                    "price": 2200,
                    "color": "Коричневый",
                    "anticat": true
                  }
                }
                ```
                """;

            var (resp, valid) = AiCommandParser.TryParse(json, "измени 2 строку");
            Assert.True(valid);
            Assert.NotNull(resp.Action);
            Assert.Equal(AiCommandType.UpdateItemByIndex, resp.Action!.Type);
            Assert.Equal(2, resp.Action.Params.ItemIndex);
            Assert.Equal(750, resp.Action.Params.UpdateWidth);
            Assert.Equal(1450, resp.Action.Params.UpdateHeight);
            Assert.Equal(3, resp.Action.Params.UpdateQuantity);
            Assert.Equal(2200, resp.Action.Params.UpdatePrice);
            Assert.Equal("Коричневый", resp.Action.Params.UpdateColor);
            Assert.True(resp.Action.Params.IsAnticat);
        }

        [Fact]
        public void ParseDeleteItemByIndex_ParsesIndex()
        {
            var json = """
                ```json
                {
                  "action": "delete_item_by_index",
                  "params": {
                    "index": 4
                  }
                }
                ```
                """;

            var (resp, valid) = AiCommandParser.TryParse(json, "удали строку 4");
            Assert.True(valid);
            Assert.NotNull(resp.Action);
            Assert.Equal(AiCommandType.DeleteItemByIndex, resp.Action!.Type);
            Assert.Equal(4, resp.Action.Params.ItemIndex);
        }

        [Fact]
        public void ParseToggleItemActive_ParsesIndexAndActive()
        {
            var json = """
                ```json
                {
                  "action": "toggle_item_active",
                  "params": {
                    "index": 1,
                    "active": false
                  }
                }
                ```
                """;

            var (resp, valid) = AiCommandParser.TryParse(json, "выключи 1 позицию");
            Assert.True(valid);
            Assert.NotNull(resp.Action);
            Assert.Equal(AiCommandType.ToggleItemActive, resp.Action!.Type);
            Assert.Equal(1, resp.Action.Params.ItemIndex);
            Assert.False(resp.Action.Params.IsActive);
        }

        [Fact]
        public void ParseSetClientInfo_ParsesAllFields()
        {
            var json = """
                ```json
                {
                  "action": "set_client_info",
                  "params": {
                    "phone": "+7 900 123-45-67",
                    "address": "ул. Пушкина 10",
                    "contract": "123-A",
                    "notes": "Позвонить перед выездом"
                  }
                }
                ```
                """;

            var (resp, valid) = AiCommandParser.TryParse(json, "заполни клиента");
            Assert.True(valid);
            Assert.NotNull(resp.Action);
            Assert.Equal(AiCommandType.SetClientInfo, resp.Action!.Type);
            Assert.Equal("+7 900 123-45-67", resp.Action.Params.ClientPhone);
            Assert.Equal("ул. Пушкина 10", resp.Action.Params.ClientAddress);
            Assert.Equal("123-A", resp.Action.Params.ContractNumber);
            Assert.Equal("Позвонить перед выездом", resp.Action.Params.Notes);
        }

        [Fact]
        public void ParseApplyTemplate_ParsesTemplateIdAndDims()
        {
            var json = """
                ```json
                {
                  "action": "apply_template",
                  "params": {
                    "template": "window",
                    "width": 650,
                    "height": 1350,
                    "color": "Белый"
                  }
                }
                ```
                """;

            var (resp, valid) = AiCommandParser.TryParse(json, "примени шаблон окно");
            Assert.True(valid);
            Assert.NotNull(resp.Action);
            Assert.Equal(AiCommandType.ApplyTemplate, resp.Action!.Type);
            Assert.Equal("window", resp.Action.Params.TemplateId);
            Assert.Equal(650, resp.Action.Params.Width);
            Assert.Equal(1350, resp.Action.Params.Height);
        }

        [Fact]
        public void ParseDocumentActions_SavePrintPdf()
        {
            var (respSave, validSave) = AiCommandParser.TryParse("""{"action":"save_order"}""", "сохрани");
            Assert.True(validSave);
            Assert.NotNull(respSave.Action);
            Assert.Equal(AiCommandType.SaveOrder, respSave.Action!.Type);

            var (respPrint, validPrint) = AiCommandParser.TryParse("""{"action":"print_kp"}""", "распечатай");
            Assert.True(validPrint);
            Assert.NotNull(respPrint.Action);
            Assert.Equal(AiCommandType.PrintKp, respPrint.Action!.Type);

            var (respPdf, validPdf) = AiCommandParser.TryParse("""{"action":"export_pdf"}""", "экспорт в pdf");
            Assert.True(validPdf);
            Assert.NotNull(respPdf.Action);
            Assert.Equal(AiCommandType.ExportPdf, respPdf.Action!.Type);
        }

        [Fact]
        public void PlanMode_MultiStepWithAllNewActions_ParsesCorrectly()
        {
            var json = """
                ```json
                {
                  "mode": "plan",
                  "reply": "План действий:",
                  "steps": [
                    { "action": "set_client_info", "params": { "address": "ул. Гагарина 5" } },
                    { "action": "apply_template", "params": { "template": "balcony", "width": 700, "height": 1900 } },
                    { "action": "update_item_by_index", "params": { "index": 1, "anticat": true } },
                    { "action": "save_order" }
                  ]
                }
                ```
                """;

            var (resp, valid) = AiCommandParser.TryParse(json, "план остекления");
            Assert.True(valid);
            Assert.NotNull(resp.Plan);
            Assert.Equal(4, resp.Plan!.Steps.Count);
            Assert.Equal(AiCommandType.SetClientInfo, resp.Plan.Steps[0].CommandType);
            Assert.Equal(AiCommandType.ApplyTemplate, resp.Plan.Steps[1].CommandType);
            Assert.Equal(AiCommandType.UpdateItemByIndex, resp.Plan.Steps[2].CommandType);
            Assert.Equal(AiCommandType.SaveOrder, resp.Plan.Steps[3].CommandType);
        }

        [Fact]
        public void AiPlanValidator_ValidatesTemplatesAndIndices()
        {
            var invalidIndexCmd = new AiCommand
            {
                Type = AiCommandType.UpdateItemByIndex,
                Params = new AiCommandParams { ItemIndex = 0, UpdateWidth = 500 }
            };
            var resIndex = AiPlanValidator.ValidateCommand(invalidIndexCmd);
            Assert.NotEmpty(resIndex);

            var invalidTemplateCmd = new AiCommand
            {
                Type = AiCommandType.ApplyTemplate,
                Params = new AiCommandParams { TemplateId = "unknown_template" }
            };
            var resTemplate = AiPlanValidator.ValidateCommand(invalidTemplateCmd);
            Assert.NotEmpty(resTemplate);

            var validTemplateCmd = new AiCommand
            {
                Type = AiCommandType.ApplyTemplate,
                Params = new AiCommandParams { TemplateId = "window" }
            };
            var resValidTemplate = AiPlanValidator.ValidateCommand(validTemplateCmd);
            Assert.Empty(resValidTemplate);
        }

        [Fact]
        public void AiCommandExecutor_ExecutesRowIndexUpdates_AndAppliesAnticat()
        {
            var calcVm = new CalculationViewModel();
            var item = calcVm.AddItem("Anwis", "Белый", 500, 1000, 1, 1800, AnwisSizeMode.Брусбокс60);
            Assert.NotNull(item);
            Assert.False(item.IsAnticat);

            var executor = new AiCommandExecutor(
                calcVm,
                pushUndo: () => { },
                markDirty: () => { },
                recalculateAndUpdateTotal: () => { },
                showToast: (_, _) => { },
                isAiOverlayVisible: () => false,
                closeAiAssistant: () => { },
                openSlopeOverlay: (_, _, _, _) => { });

            var updateCmd = new AiCommand
            {
                Type = AiCommandType.UpdateItemByIndex,
                Params = new AiCommandParams
                {
                    ItemIndex = 1,
                    UpdateWidth = 600,
                    UpdateHeight = 1200,
                    IsAnticat = true
                }
            };

            bool ok = executor.Execute(updateCmd, pushUndo: false, out var error);
            Assert.True(ok, error);
            Assert.Equal(600, item.Width);
            Assert.Equal(1200, item.Height);
            Assert.True(item.IsAnticat);
            Assert.True(item.AnticatSurchargeTotal > 0);
        }

        [Fact]
        public void AiCommandExecutor_ExecutesTemplateApplication()
        {
            var calcVm = new CalculationViewModel();
            var executor = new AiCommandExecutor(
                calcVm,
                pushUndo: () => { },
                markDirty: () => { },
                recalculateAndUpdateTotal: () => { },
                showToast: (_, _) => { },
                isAiOverlayVisible: () => false,
                closeAiAssistant: () => { },
                openSlopeOverlay: (_, _, _, _) => { });

            var templateCmd = new AiCommand
            {
                Type = AiCommandType.ApplyTemplate,
                Params = new AiCommandParams
                {
                    TemplateId = "window",
                    Width = 700,
                    Height = 1400
                }
            };

            bool ok = executor.Execute(templateCmd, pushUndo: false, out var error);
            Assert.True(ok, error);
            Assert.True(calcVm.OrderItems.Count >= 2);
        }

        [Fact]
        public void AiCommandExecutor_ExecutesClientInfoAndDocActions()
        {
            var calcVm = new CalculationViewModel();
            string capturedPhone = "";
            bool saved = false;
            bool printed = false;
            bool pdfExported = false;

            var executor = new AiCommandExecutor(
                calcVm,
                pushUndo: () => { },
                markDirty: () => { },
                recalculateAndUpdateTotal: () => { },
                showToast: (_, _) => { },
                isAiOverlayVisible: () => false,
                closeAiAssistant: () => { },
                openSlopeOverlay: (_, _, _, _) => { },
                setClientInfo: (phone, _, _, _) => capturedPhone = phone,
                saveOrder: () => saved = true,
                printKp: () => printed = true,
                exportPdf: () => pdfExported = true);

            var clientCmd = new AiCommand
            {
                Type = AiCommandType.SetClientInfo,
                Params = new AiCommandParams { ClientPhone = "+7 999 000-11-22" }
            };
            Assert.True(executor.Execute(clientCmd, pushUndo: false, out _));
            Assert.Equal("+7 999 000-11-22", capturedPhone);

            Assert.True(executor.Execute(new AiCommand { Type = AiCommandType.SaveOrder }, pushUndo: false, out _));
            Assert.True(saved);

            Assert.True(executor.Execute(new AiCommand { Type = AiCommandType.PrintKp }, pushUndo: false, out _));
            Assert.True(printed);

            Assert.True(executor.Execute(new AiCommand { Type = AiCommandType.ExportPdf }, pushUndo: false, out _));
            Assert.True(pdfExported);
        }
    }
}
