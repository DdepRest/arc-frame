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
            Assert.Equal("ББ 60", form.SelectedAnwisMode);
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

            Assert.Equal(new[] { "Отлив" }, form.ProductTypes);
            Assert.Equal("Отлив", form.SelectedType);
            Assert.False(form.IsAnwis);
        }

        [Fact]
        public void Request_Kozyrek_OffersOnlyKozyrek()
        {
            var form = new AiClarificationForm("сделай козырёк");

            Assert.Equal(new[] { "Козырёк" }, form.ProductTypes);
        }

        [Fact]
        public void Request_Korob_OffersOnlyKorob()
        {
            var form = new AiClarificationForm("короб на окно");

            Assert.Equal(new[] { "Короб" }, form.ProductTypes);
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

            Assert.Equal(new[] { "Уплотнение" }, form.ProductTypes);
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
    }
}
