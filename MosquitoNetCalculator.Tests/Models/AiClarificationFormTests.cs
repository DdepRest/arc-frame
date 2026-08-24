using System;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Models
{
    public sealed class AiClarificationFormTests
    {
        [Theory]
        [InlineData("Уточните, пожалуйста: 1. Какой тип сетки?")]
        [InlineData("Укажите ширину и высоту окна в мм.")]
        [InlineData("Напишите параметры — и я добавлю позицию.")]
        [InlineData("Выберите тип: Anwis, На навесах…")]
        [InlineData("⚠ Для Anwis укажите режим: ББ60, ББ70, ПП, Проём или Габарит.")]
        [InlineData("Для добавления сетки Anwis мне всё ещё необходимо знать режим. Какой режим использовать? • ББ60 • ББ70 • ПП • Проём • Габарит")]
        [InlineData("Какой режим использовать — ББ60, ББ70, ПП, Проём или Габарит?")]
        [InlineData("Какой профиль выбрать?")]
        [InlineData("Какие размеры сетки?")]
        [InlineData("Какой цвет предпочитаете?")]
        [InlineData("Не хватает глубины откоса.")]
        public void LooksLikeClarification_DetectsParameterRequests(string text)
        {
            Assert.True(AiClarificationForm.LooksLikeClarification(text));
        }

        [Theory]
        [InlineData("Готово, добавил сетку Anwis 700×1400.")]
        [InlineData("Вот список товаров: Anwis, Отлив, Козырёк.")]
        [InlineData("Здравствуйте! Чем могу помочь?")]
        [InlineData("")]
        [InlineData(null)]
        public void LooksLikeClarification_IgnoresOrdinaryReplies(string? text)
        {
            Assert.False(AiClarificationForm.LooksLikeClarification(text));
        }

        [Fact]
        public void Defaults_AnwisWhite_BB60_NoInstallation()
        {
            var form = new AiClarificationForm();

            Assert.Equal("Anwis", form.SelectedType);
            Assert.True(form.IsAnwis);
            Assert.True(form.ShowColor);
            Assert.True(form.ShowInstallation);
            Assert.Contains("Белый", form.Colors);
            Assert.Equal("Белый", form.SelectedColor);
            // The profile is a critical parameter — it stays unselected until the user picks one.
            Assert.Equal(AiClarificationForm.UnspecifiedAnwisMode, form.SelectedAnwisMode);
        }

        [Fact]
        public void Defaults_AllProductTypes_WhenNoRequest()
        {
            var form = new AiClarificationForm();

            Assert.Equal(AiClarificationForm.AllProductTypes, form.ProductTypes);
            Assert.Contains("Отлив", form.ProductTypes);
            Assert.Contains("Козырёк", form.ProductTypes);
        }

        [Theory]
        [InlineData("Сделай сетку", "Anwis")]
        [InlineData("Сделай сетку", "На навесах")]
        [InlineData("Сделай сетку", "Оконная на метал. крепл.")]
        [InlineData("Сделай сетку", "Дверная сетка")]
        [InlineData("Сделай сетку", null)] // mesh keyword only
        public void Request_Mesh_OffersOnlyMeshProducts(string request, string? expectedProduct)
        {
            var form = new AiClarificationForm(request);

            Assert.DoesNotContain("Отлив", form.ProductTypes);
            Assert.DoesNotContain("Козырёк", form.ProductTypes);
            Assert.DoesNotContain("ПСУЛ", form.ProductTypes);
            if (expectedProduct != null)
                Assert.Contains(expectedProduct, form.ProductTypes);
        }

        [Fact]
        public void Request_Otlivo_OffersOnlyOtlivo()
        {
            var form = new AiClarificationForm("добавь отлив");

            Assert.Equal(new[] { "Отлив", AiClarificationForm.CustomProductType }, form.ProductTypes);
            Assert.Equal("Отлив", form.SelectedType);
            Assert.False(form.IsAnwis);
        }

        [Fact]
        public void Request_Kozyrek_OffersOnlyKozyrek()
        {
            var form = new AiClarificationForm("сделай козырёк");

            Assert.Equal(new[] { "Козырёк", AiClarificationForm.CustomProductType }, form.ProductTypes);
        }

        [Fact]
        public void Request_Korob_OffersOnlyKorob()
        {
            var form = new AiClarificationForm("короб на окно");

            Assert.Equal(new[] { "Короб", AiClarificationForm.CustomProductType }, form.ProductTypes);
        }

        [Fact]
        public void Request_MultipleFamilies_OffersUnion()
        {
            var form = new AiClarificationForm("сделай короб для отлива");

            Assert.Contains("Короб", form.ProductTypes);
            Assert.Contains("Отлив", form.ProductTypes);
        }

        [Fact]
        public void Request_Uplotnenie_OffersOnlyUplotnenie()
        {
            var form = new AiClarificationForm("уплотнение для двери");

            Assert.Equal(new[] { "Уплотнение", AiClarificationForm.CustomProductType }, form.ProductTypes);
        }

        [Fact]
        public void Request_Unknown_OffersFullCatalog()
        {
            var form = new AiClarificationForm("помогите с расчётом");

            Assert.Equal(AiClarificationForm.AllProductTypes, form.ProductTypes);
        }

        [Fact]
        public void FilterProductsForRequest_NullOrEmpty_ReturnsFullCatalog()
        {
            Assert.Equal(AiClarificationForm.AllProductTypes, AiClarificationForm.FilterProductsForRequest(null));
            Assert.Equal(AiClarificationForm.AllProductTypes, AiClarificationForm.FilterProductsForRequest(""));
        }

        [Fact]
        public void Request_WithSizeColorAndCount_PrefillsKnownFields()
        {
            var form = new AiClarificationForm("ПМС Anwis. бел\n4 739х1116");

            Assert.Equal("Anwis", form.SelectedType);
            Assert.Equal("Белый", form.SelectedColor);
            Assert.Equal("739", form.WidthText);
            Assert.Equal("1116", form.HeightText);
            Assert.Equal("4", form.QuantityText);
            // The still-missing profile (ПП/ББ60…) stays unselected — the user
            // must pick that one field before submitting.
            Assert.Equal(AiClarificationForm.UnspecifiedAnwisMode, form.SelectedAnwisMode);
        }

        [Fact]
        public void Request_ReportedPhrase_PrefillsEverythingButMode()
        {
            var form = new AiClarificationForm("Добавь сетку Anwis белый 739×1116 4 шт");

            Assert.Equal("Anwis", form.SelectedType);
            Assert.Equal("Белый", form.SelectedColor);
            Assert.Equal("739", form.WidthText);
            Assert.Equal("1116", form.HeightText);
            Assert.Equal("4", form.QuantityText);
            Assert.Equal(AiClarificationForm.UnspecifiedAnwisMode, form.SelectedAnwisMode);
        }

        [Fact]
        public void KnownParams_PrefillsCard_WhenUserTextIsSparse()
        {
            var form = new AiClarificationForm(
                "сделай сетку",
                new AiCommandParams { Type = "Anwis", Color = "Коричневый", Width = 619, Height = 1295, Quantity = 2 });

            Assert.Equal("Anwis", form.SelectedType);
            Assert.Equal("Коричневый", form.SelectedColor);
            Assert.Equal("619", form.WidthText);
            Assert.Equal("1295", form.HeightText);
            Assert.Equal("2", form.QuantityText);
            // The guessed profile is never copied — the user must re-pick it.
            Assert.Equal(AiClarificationForm.UnspecifiedAnwisMode, form.SelectedAnwisMode);
        }

        [Fact]
        public void KnownParams_UserTextOverridesExplicitFields()
        {
            var form = new AiClarificationForm(
                "Anwis белый 700х1400",
                new AiCommandParams { Type = "Anwis", Color = "Коричневый", Width = 619, Height = 1295, Quantity = 2 });

            Assert.Equal("Белый", form.SelectedColor);
            Assert.Equal("700", form.WidthText);
            Assert.Equal("1400", form.HeightText);
        }

        [Theory]
        [InlineData("Добавь сетку Anwis белый 739×1116 4 шт", "Для добавления сетки Anwis мне всё ещё необходимо знать режим. Какой режим использовать?", true)]
        [InlineData("Добавь сетку Anwis белый 739×1116 4 шт", "Готово, добавил.", true)] // local fallback: request lacks the mode
        [InlineData("Добавь сетку Anwis белый 739×1116 ББ60", "Готово, добавил.", false)]
        [InlineData("Сколько стоит Anwis 739×1116?", "Anwis стоит 1800 ₽/м².", false)] // no add-intent verb
        [InlineData("Добавь отлив 200×1500", "Готово, добавил.", false)] // not a mesh product
        [InlineData("Добавь сетку Anwis", "Укажите режим Anwis.", true)] // reply asks for it
        [InlineData(null, null, false)]
        [InlineData("", "", false)]
        public void ShouldShowForm_DetectsClarificationOrIncompleteAnwisAdd(
            string? request, string? reply, bool expected)
        {
            Assert.Equal(expected, AiClarificationForm.ShouldShowForm(request, reply));
        }

        [Theory]
        [InlineData("Anwis бб60", true)]
        [InlineData("Anwis ББ 70", true)]
        [InlineData("профипласт 739×1116", true)]
        [InlineData("проём", true)]
        [InlineData("габарит", true)]
        [InlineData("Anwis белый 739×1116", false)]
        [InlineData("ПМС Anwis. бел 4 739х1116", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void AnwisModeSpecified_DetectsModeKeywords(string? text, bool expected)
        {
            Assert.Equal(expected, AiClarificationForm.AnwisModeSpecified(text));
        }

        [Fact]
        public void ShouldAskAnwisModeFor_AnwisAddWithoutUserMode_ReturnsTrue()
        {
            var commands = new[]
            {
                new AiCommand
                {
                    Type = AiCommandType.AddItem,
                    Params = new AiCommandParams { Type = "Anwis", Width = 739, Height = 1116, Quantity = 4 }
                }
            };

            Assert.True(AiClarificationForm.ShouldAskAnwisModeFor(commands, "ПМС Anwis. бел 4 739х1116"));
        }

        [Fact]
        public void ShouldAskAnwisModeFor_UserSpecifiedMode_ReturnsFalse()
        {
            var commands = new[]
            {
                new AiCommand
                {
                    Type = AiCommandType.AddItem,
                    Params = new AiCommandParams { Type = "Anwis", Width = 739, Height = 1116, AnwisMode = AnwisSizeMode.Профипласт }
                }
            };

            Assert.False(AiClarificationForm.ShouldAskAnwisModeFor(commands, "Добавь сетку Anwis белый 739×1116 ПП"));
        }

        [Fact]
        public void ShouldAskAnwisModeFor_NonAnwisAdd_ReturnsFalse()
        {
            var commands = new[]
            {
                new AiCommand
                {
                    Type = AiCommandType.AddItem,
                    Params = new AiCommandParams { Type = "Отлив", Width = 200, Height = 1500 }
                }
            };

            Assert.False(AiClarificationForm.ShouldAskAnwisModeFor(commands, "Добавь отлив 200×1500"));
        }

        [Fact]
        public void ShouldAskForMissingParams_AnwisWithoutMode_ReturnsTrue()
        {
            var commands = new[]
            {
                new AiCommand
                {
                    Type = AiCommandType.AddItem,
                    Params = new AiCommandParams { Type = "Anwis", Width = 739, Height = 1116 }
                }
            };

            Assert.True(AiClarificationForm.ShouldAskForMissingParams(commands, "ПМС Anwis. бел 4 739х1116"));
        }

        [Fact]
        public void ShouldAskForMissingParams_AnwisWithModeAndInstallation_ReturnsFalse()
        {
            var commands = new[]
            {
                new AiCommand
                {
                    Type = AiCommandType.AddItem,
                    Params = new AiCommandParams { Type = "Anwis", Width = 739, Height = 1116, AnwisMode = AnwisSizeMode.Профипласт, InstallationMode = 0 }
                }
            };

            Assert.False(AiClarificationForm.ShouldAskForMissingParams(commands, "Добавь сетку Anwis белый 739×1116 ПП с монтажом"));
        }

        [Fact]
        public void ShouldAskForMissingParams_AnwisWithModeButNoInstallation_ReturnsTrue()
        {
            var commands = new[]
            {
                new AiCommand
                {
                    Type = AiCommandType.AddItem,
                    Params = new AiCommandParams { Type = "Anwis", Width = 739, Height = 1116, AnwisMode = AnwisSizeMode.Профипласт }
                }
            };

            Assert.True(AiClarificationForm.ShouldAskForMissingParams(commands, "Добавь сетку Anwis белый 739×1116 ПП"));
        }

        [Fact]
        public void ShouldAskForMissingParams_NonAnwisMissingDimensions_ReturnsTrue()
        {
            var commands = new[]
            {
                new AiCommand
                {
                    Type = AiCommandType.AddItem,
                    Params = new AiCommandParams { Type = "Отлив", Color = "Белый" }
                }
            };

            Assert.True(AiClarificationForm.ShouldAskForMissingParams(commands, "добавь отлив белый"));
        }

        [Fact]
        public void ShouldAskForMissingParams_CompleteAddWithoutInstallationToggle_ReturnsFalse()
        {
            // Короб is not installation-applicable, so a complete add runs without
            // asking about монтаж.
            var commands = new[]
            {
                new AiCommand
                {
                    Type = AiCommandType.AddItem,
                    Params = new AiCommandParams { Type = "Короб", Color = "Белый", Width = 200, Height = 1500 }
                }
            };

            Assert.False(AiClarificationForm.ShouldAskForMissingParams(commands, "Добавь короб белый 200×1500"));
        }

        [Fact]
        public void ShouldAskForMissingParams_InstallationApplicableWithoutInstallation_ReturnsTrue()
        {
            var commands = new[]
            {
                new AiCommand
                {
                    Type = AiCommandType.AddItem,
                    Params = new AiCommandParams { Type = "Отлив", Color = "Белый", Width = 170, Height = 900 }
                }
            };

            Assert.True(AiClarificationForm.ShouldAskForMissingParams(commands, "отлив бел 170 900"));
        }

        [Fact]
        public void ShouldAskForMissingParams_InstallationSpecified_ReturnsFalse()
        {
            var commands = new[]
            {
                new AiCommand
                {
                    Type = AiCommandType.AddItem,
                    Params = new AiCommandParams { Type = "Отлив", Color = "Белый", Width = 170, Height = 900, InstallationMode = 0 }
                }
            };

            Assert.False(AiClarificationForm.ShouldAskForMissingParams(commands, "отлив бел 170 900 с монтажом"));
        }

        [Theory]
        [InlineData("с монтажом", 0)]
        [InlineData("С МОНТАЖОМ", 0)]
        [InlineData("монтаж включён", 0)]
        [InlineData("без монтажа", 1)]
        [InlineData("без установки", 1)]
        [InlineData("в конструкцию", 2)]
        [InlineData("в конструцию", 2)]
        [InlineData("конструкция", 2)]
        [InlineData("просто отлив", -1)]
        [InlineData("", -1)]
        [InlineData(null, -1)]
        public void DetectInstallationMode_MapsKeywords(string? text, int expected)
        {
            Assert.Equal(expected, AiClarificationForm.DetectInstallationMode(text));
        }

        [Theory]
        [InlineData("отлив с монтажом", true)]
        [InlineData("козырёк без монтажа", true)]
        [InlineData("анвис в конструкцию", true)]
        [InlineData("отлив бел 170 900", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void InstallationModeSpecified_DetectsKeywords(string? text, bool expected)
        {
            Assert.Equal(expected, AiClarificationForm.InstallationModeSpecified(text));
        }

        [Theory]
        [InlineData("отлив бел 170 900 с монтажом", "С монтажом")]
        [InlineData("козырёк 350×2300 без монтажа", "Без монтажа")]
        [InlineData("анвис в конструкцию", "В конструкцию")]
        public void Request_WithInstallation_PrefillsInstallation(string request, string expected)
        {
            var form = new AiClarificationForm(request);

            Assert.Equal(expected, form.SelectedInstallation);
        }

        [Fact]
        public void KnownParams_NonAnwisPrefillsCard()
        {
            var form = new AiClarificationForm(
                "добавь отлив",
                new AiCommandParams { Type = "Отлив", Color = "Антрацит", Width = 2000, Height = 120, Quantity = 3 });

            Assert.Equal("Отлив", form.SelectedType);
            Assert.Equal("Антрацит", form.SelectedColor);
            Assert.Equal("2000", form.WidthText);
            Assert.Equal("120", form.HeightText);
            Assert.Equal("3", form.QuantityText);
        }

        [Theory]
        [InlineData("739х1116", "739", "1116")]
        [InlineData("739x1116", "739", "1116")]
        [InlineData("739×1116", "739", "1116")]
        [InlineData("739 х 1116", "739", "1116")]
        [InlineData("739 * 1116", "739", "1116")]
        [InlineData("400x1500", "400", "1500")]
        [InlineData("1500x400", "1500", "400")]
        public void Request_DimensionSeparators_PrefillWidthAndHeight(
            string size, string expectedWidth, string expectedHeight)
        {
            var form = new AiClarificationForm($"Anwis белый {size}");

            Assert.Equal(expectedWidth, form.WidthText);
            Assert.Equal(expectedHeight, form.HeightText);
        }

        [Fact]
        public void Request_CompactOcrNumber_DoesNotGuessDimensions_ButKeepsOtherFields()
        {
            var form = new AiClarificationForm("ПМС Anwis, бел. 2 шт 3711217");

            Assert.Equal("", form.WidthText);
            Assert.Equal("", form.HeightText);
            Assert.Equal("Белый", form.SelectedColor);
            Assert.Equal("2", form.QuantityText);
        }

        [Fact]
        public void Request_WithShtQuantity_PrefillsQuantity()
        {
            var form = new AiClarificationForm("Anwis белый 2 шт 739х1116");

            Assert.Equal("2", form.QuantityText);
            Assert.Equal("739", form.WidthText);
            Assert.Equal("1116", form.HeightText);
        }

        [Theory]
        [InlineData("Anwis бб70 739х1116", "ББ 70")]
        [InlineData("Anwis ПП 739х1116", "ПП")]
        [InlineData("Anwis проём 739х1116", "Проём")]
        [InlineData("Anwis габарит 739х1116", "Габарит")]
        public void Request_WithAnwisMode_PrefillsMode(string request, string expectedMode)
        {
            var form = new AiClarificationForm(request);

            Assert.Equal(expectedMode, form.SelectedAnwisMode);
        }

        [Fact]
        public void Request_WithNoSize_LeavesDimensionsEmpty()
        {
            var form = new AiClarificationForm("Anwis белый бб60");

            Assert.Equal("", form.WidthText);
            Assert.Equal("", form.HeightText);
            Assert.Equal("Белый", form.SelectedColor);
            Assert.Equal("ББ 60", form.SelectedAnwisMode);
        }

        [Fact]
        public void Request_ColorMatchesSelectedProductPalette()
        {
            var form = new AiClarificationForm("уплотнение серый 1000х20");

            Assert.Equal("Уплотнение", form.SelectedType);
            Assert.Equal("Серый", form.SelectedColor);
        }

        [Fact]
        public void TypeChange_AnwisToOtlivo_UpdatesFlagsAndColors()
        {
            var form = new AiClarificationForm { SelectedType = "Отлив" };

            Assert.False(form.IsAnwis);
            Assert.True(form.ShowColor);
            Assert.True(form.ShowInstallation);
            Assert.Contains("Антрацит", form.Colors);
            Assert.Contains("Золотой дуб", form.Colors);
        }

        [Fact]
        public void TypeChange_ToNoColorProduct_HidesColor()
        {
            var form = new AiClarificationForm { SelectedType = "ПСУЛ" };

            Assert.False(form.IsAnwis);
            Assert.False(form.ShowColor);
            Assert.False(form.ShowInstallation);
            Assert.Empty(form.Colors);
        }

        [Fact]
        public void TryBuildCommand_Anwis_ProducesAddItemCommand()
        {
            var form = new AiClarificationForm
            {
                SelectedType = "Anwis",
                SelectedColor = "Коричневый",
                WidthText = "700",
                HeightText = "1400",
                QuantityText = "2",
                SelectedAnwisMode = "Проём",
                SelectedInstallation = "В конструкцию"
            };

            Assert.True(form.TryBuildCommand(out var command, out var error));
            Assert.Null(error);
            Assert.NotNull(command);
            Assert.Equal(AiCommandType.AddItem, command!.Type);
            Assert.Equal("Anwis", command.Params.Type);
            Assert.Equal("Коричневый", command.Params.Color);
            Assert.Equal(700, command.Params.Width);
            Assert.Equal(1400, command.Params.Height);
            Assert.Equal(2, command.Params.Quantity);
            Assert.Equal(AnwisSizeMode.РазмерПроёма, command.Params.AnwisMode);
            Assert.Equal(2, command.Params.InstallationMode);
            Assert.Equal(1900, command.Params.Price); // Коричневый Anwis
        }

        [Fact]
        public void TryBuildCommand_UnspecifiedAnwisMode_ReturnsError()
        {
            var form = new AiClarificationForm
            {
                SelectedType = "Anwis",
                WidthText = "739",
                HeightText = "1116",
                QuantityText = "4"
            };

            Assert.False(form.TryBuildCommand(out var command, out var error));
            Assert.Null(command);
            Assert.Contains("режим Anwis", error, StringComparison.OrdinalIgnoreCase);

            // Picking a real profile unlocks the command.
            form.SelectedAnwisMode = "ПП";
            Assert.True(form.TryBuildCommand(out command, out error));
            Assert.Equal(AnwisSizeMode.Профипласт, command!.Params.AnwisMode);
        }

        [Fact]
        public void TryBuildCommand_MissingDimensions_ReturnsError()
        {
            var form = new AiClarificationForm { WidthText = "", HeightText = "1000" };

            Assert.False(form.TryBuildCommand(out var command, out var error));
            Assert.Null(command);
            Assert.NotNull(error);
            Assert.Contains("ширину", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TryBuildCommand_ZeroQuantity_ReturnsError()
        {
            var form = new AiClarificationForm
            {
                WidthText = "700",
                HeightText = "1400",
                QuantityText = "0"
            };

            Assert.False(form.TryBuildCommand(out var command, out var error));
            Assert.Null(command);
            Assert.Contains("Количество", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildSummaryText_IncludesAllChosenParameters()
        {
            var form = new AiClarificationForm
            {
                SelectedType = "Anwis",
                SelectedColor = "Коричневый",
                WidthText = "700",
                HeightText = "1400",
                QuantityText = "2",
                SelectedAnwisMode = "ББ 60",
                SelectedInstallation = "С монтажом"
            };

            var summary = form.BuildSummaryText();

            Assert.Contains("Anwis", summary);
            Assert.Contains("Коричневый", summary);
            Assert.Contains("700×1400", summary);
            Assert.Contains("2 шт.", summary);
            Assert.Contains("ББ 60", summary);
            Assert.Contains("с монтажом", summary);
        }

        [Fact]
        public void BuildSummaryText_SingleQuantity_OmitsCount()
        {
            var form = new AiClarificationForm
            {
                WidthText = "700",
                HeightText = "1400",
                QuantityText = "1"
            };

            var summary = form.BuildSummaryText();

            Assert.DoesNotContain("шт.", summary);
        }

        [Fact]
        public void CustomProduct_AppearsInAllProductTypes()
        {
            Assert.Contains(AiClarificationForm.CustomProductType, AiClarificationForm.AllProductTypes);
        }

        [Fact]
        public void CustomProduct_IsCustom_True_WhenSelected()
        {
            var form = new AiClarificationForm { SelectedType = AiClarificationForm.CustomProductType };

            Assert.True(form.IsCustom);
            Assert.True(form.ShowCustomName);
            Assert.False(form.ShowColor);
            Assert.True(form.ShowInstallation);
            Assert.False(form.IsAnwis);
        }

        [Fact]
        public void CustomProduct_TryBuildCommand_RequiresName()
        {
            var form = new AiClarificationForm
            {
                SelectedType = AiClarificationForm.CustomProductType,
                WidthText = "100",
                HeightText = "200",
                QuantityText = "1"
            };

            Assert.False(form.TryBuildCommand(out _, out var error));
            Assert.Contains("название", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CustomProduct_TryBuildCommand_Succeeds_WithNameAndDimensions()
        {
            var form = new AiClarificationForm
            {
                SelectedType = AiClarificationForm.CustomProductType,
                CustomNameText = "Шуруп 4×30",
                WidthText = "100",
                HeightText = "200",
                QuantityText = "3"
            };

            Assert.True(form.TryBuildCommand(out var cmd, out _));
            Assert.NotNull(cmd);
            Assert.Equal("Шуруп 4×30", cmd.Params.Type);
            Assert.Equal(100, cmd.Params.Width);
            Assert.Equal(200, cmd.Params.Height);
            Assert.Equal(3, cmd.Params.Quantity);
        }

        [Fact]
        public void CustomProduct_TryBuildCommand_Succeeds_WithOnlyOneDimension()
        {
            var form = new AiClarificationForm
            {
                SelectedType = AiClarificationForm.CustomProductType,
                CustomNameText = "Лента",
                WidthText = "5000",
                HeightText = "",
                QuantityText = "1"
            };

            Assert.True(form.TryBuildCommand(out var cmd, out _));
            Assert.NotNull(cmd);
            Assert.Equal("Лента", cmd.Params.Type);
            Assert.Equal(5000, cmd.Params.Width);
            Assert.Equal(0, cmd.Params.Height); // empty height stays empty (never 1×1)
        }

        [Fact]
        public void CustomProduct_BuildSummaryText_UsesCustomName()
        {
            var form = new AiClarificationForm
            {
                SelectedType = AiClarificationForm.CustomProductType,
                CustomNameText = "Герметик",
                WidthText = "",
                HeightText = "",
                QuantityText = "1"
            };

            var summary = form.BuildSummaryText();

            Assert.Contains("Герметик", summary);
            Assert.DoesNotContain("Свой товар", summary);
        }

        [Fact]
        public void CustomProduct_TryBuildCommand_EmptyDimsAndQty_ManualSum()
        {
            // User's rule: for «Свой товар» empty width/height stay empty (0,
            // never substituted with 1×1) and empty quantity defaults to 0 —
            // optional, so the row shows the manually entered price as the sum.
            var form = new AiClarificationForm
            {
                SelectedType = AiClarificationForm.CustomProductType,
                CustomNameText = "Герметик",
                WidthText = "",
                HeightText = "",
                QuantityText = ""
            };

            Assert.True(form.TryBuildCommand(out var cmd, out _));
            Assert.NotNull(cmd);
            Assert.True(cmd.Params.IsCustomProduct);
            Assert.Equal(0, cmd.Params.Width);
            Assert.Equal(0, cmd.Params.Height);
            Assert.Equal(0, cmd.Params.Quantity); // empty qty → 0 (optional) → Total = Price
        }

        [Fact]
        public void FilterProductsForRequest_AlwaysIncludesCustomProduct()
        {
            var filtered = AiClarificationForm.FilterProductsForRequest("отлив");

            Assert.Contains(AiClarificationForm.CustomProductType, filtered);
            Assert.Contains("Отлив", filtered);
        }
    }
}
