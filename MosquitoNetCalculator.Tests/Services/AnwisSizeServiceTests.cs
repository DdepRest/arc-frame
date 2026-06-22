using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class AnwisSizeServiceTests
    {
        // ────────────────────────────────────────────────────────────────
        // GetExplanation — числа для примера 1000×1000 на каждом из 5 режимов.
        // Числа жёстко зафиксированы по текущим формулам AnwisSize.cs:
        //   ББ 60: W+2, H−30, завод −20
        //   ББ 70: W−2, H−30, завод −20
        //   ПП:    identity, завод −20
        //   Проём: W+20, H+20, завод −20
        //   Габар: identity, завод −20
        // Сменилась формула → меняются ожидаемые числа ниже.
        // ────────────────────────────────────────────────────────────────

        [Fact]
        public void GetExplanation_Brusbox60_Produces1002x970_and982x950()
        {
            var (input, calc, factory) = AnwisSizeService.GetExplanation(AnwisSizeMode.Брусбокс60);

            Assert.Equal("Ввод: размеры, которые показывает программа (то, что на экране)", input);
            Assert.Equal("Расчёт (площадь, цена): 1000×1000 → 1002×970 (W+2, H−30)", calc);
            Assert.Equal("На завод (копия): расчёт − 20 мм → 982×950", factory);
        }

        [Fact]
        public void GetExplanation_Brusbox70_Produces998x970_and978x950()
        {
            var (input, calc, factory) = AnwisSizeService.GetExplanation(AnwisSizeMode.Брусбокс70);

            Assert.Equal("Ввод: размеры, которые показывает программа (то, что на экране)", input);
            Assert.Equal("Расчёт (площадь, цена): 1000×1000 → 998×970 (W−2, H−30)", calc);
            Assert.Equal("На завод (копия): расчёт − 20 мм → 978×950", factory);
        }

        [Fact]
        public void GetExplanation_Profiplast_Identity_and_minus20_in_factory()
        {
            var (input, calc, factory) = AnwisSizeService.GetExplanation(AnwisSizeMode.Профипласт);

            Assert.Equal("Ввод: размеры, которые показывает программа (то, что на экране)", input);
            Assert.Equal("Расчёт (площадь, цена): 1000×1000 → 1000×1000 (без изменений)", calc);
            Assert.Equal("На завод (копия): расчёт − 20 мм → 980×980", factory);
        }

        [Fact]
        public void GetExplanation_Proem_UsesSealToSealText_andPlus20()
        {
            var (input, calc, factory) = AnwisSizeService.GetExplanation(AnwisSizeMode.РазмерПроёма);

            // Концептуальная подпись: чистые размеры от уплотнения до уплотнения
            Assert.Equal("Ввод: чистые размеры от уплотнения до уплотнения", input);
            Assert.Equal("Расчёт (площадь, цена): 1000×1000 → 1020×1020 (W+20, H+20)", calc);
            Assert.Equal("На завод (копия): расчёт − 20 мм → 1000×1000", factory);
        }

        [Fact]
        public void GetExplanation_Gabarit_UsesPlus20FromProemText_andIdentity()
        {
            var (input, calc, factory) = AnwisSizeService.GetExplanation(AnwisSizeMode.Габаритный);

            // Концептуальная подпись: размеры с +20 мм от проёма
            Assert.Equal("Ввод: размеры с +20 мм от проёма", input);
            Assert.Equal("Расчёт (площадь, цена): 1000×1000 → 1000×1000 (без изменений)", calc);
            Assert.Equal("На завод (копия): расчёт − 20 мм → 980×980", factory);
        }

        // ────────────────────────────────────────────────────────────────
        // Защита от регрессии: концептуальная подпись «ввод» для трёх режимов
        // с identity на стороне расчёта должна быть ОДИНАКОВОЙ.
        // Если кто-то случайно разведёт тексты — увидим сразу.
        // ────────────────────────────────────────────────────────────────

        [Fact]
        public void GetExplanation_BB_Profiplast_ShareSameInputConcept()
        {
            const string shared =
                "Ввод: размеры, которые показывает программа (то, что на экране)";

            Assert.Equal(shared, AnwisSizeService.GetExplanation(AnwisSizeMode.Брусбокс60).Input);
            Assert.Equal(shared, AnwisSizeService.GetExplanation(AnwisSizeMode.Брусбокс70).Input);
            Assert.Equal(shared, AnwisSizeService.GetExplanation(AnwisSizeMode.Профипласт).Input);
        }

        [Fact]
        public void GetExplanation_Proem_Gabarit_HaveDistinctInputConcepts()
        {
            var proem  = AnwisSizeService.GetExplanation(AnwisSizeMode.РазмерПроёма).Input;
            var gabar  = AnwisSizeService.GetExplanation(AnwisSizeMode.Габаритный).Input;

            Assert.Contains("от уплотнения до уплотнения", proem);
            Assert.Contains("+20 мм от проёма",            gabar);
            Assert.NotEqual(proem, gabar);
        }

        // ────────────────────────────────────────────────────────────────
        // Прочие API AnwisSizeService (smoke-tests, защита от регрессий).
        // ────────────────────────────────────────────────────────────────

        [Fact]
        public void ShortLabels_CoversAllFiveModes()
        {
            Assert.Equal(5, AnwisSizeService.ShortLabels.Count);
            Assert.Equal("ББ 60",   AnwisSizeService.ShortLabels[AnwisSizeMode.Брусбокс60]);
            Assert.Equal("ББ 70",   AnwisSizeService.ShortLabels[AnwisSizeMode.Брусбокс70]);
            Assert.Equal("ПП",      AnwisSizeService.ShortLabels[AnwisSizeMode.Профипласт]);
            Assert.Equal("Проём",   AnwisSizeService.ShortLabels[AnwisSizeMode.РазмерПроёма]);
            Assert.Equal("Габарит", AnwisSizeService.ShortLabels[AnwisSizeMode.Габаритный]);
        }

        [Fact]
        public void GetSectionHeader_UsesFullLabel()
        {
            var header = AnwisSizeService.GetSectionHeader(AnwisSizeMode.Брусбокс60);
            Assert.Equal("Anwis, размер проёма (Режим: Брусбокс 60)", header);
        }

        [Fact]
        public void IsApplicable_AnwisAndAnwisCaseInsensitive()
        {
            Assert.True(AnwisSizeService.IsApplicable("Anwis"));
            Assert.True(AnwisSizeService.IsApplicable("anwis"));
            Assert.True(AnwisSizeService.IsApplicable("ANWIS"));
            Assert.False(AnwisSizeService.IsApplicable("Moisquito"));
            Assert.False(AnwisSizeService.IsApplicable(null));
            Assert.False(AnwisSizeService.IsApplicable(""));
        }

        [Fact]
        public void DefaultMode_IsBrusbox60()
        {
            Assert.Equal(AnwisSizeMode.Брусбокс60, AnwisSizeService.DefaultMode);
        }
    }
}
