using MosquitoNetCalculator.Models;
using Xunit;

namespace MosquitoNetCalculator.Tests.Models
{
    public class SlopeCalculationExtensionsTests
    {
        [Fact]
        public void DeepClone_CreatesIndependentCopy()
        {
            var original = new SlopeCalculation
            {
                WidthMm = 1000,
                HeightMm = 1500,
                DepthM = 0.15,
                WindowCount = 2,
                IsManualOverride = true,
                IsProfileEconomyApplied = true,
            };
            original.Sandwich.Quantity = 1.5;
            original.Sandwich.Price = 1200;
            original.Sandwich.Unit = "м²";
            original.Sandwich.Name = "Сэндвич";
            original.Sandwich.IsQuantityOverridden = true;

            var clone = original.DeepClone();

            Assert.NotSame(original, clone);
            Assert.NotSame(original.Sandwich, clone.Sandwich);
            Assert.Equal(original.WidthMm, clone.WidthMm);
            Assert.Equal(original.HeightMm, clone.HeightMm);
            Assert.Equal(original.DepthM, clone.DepthM);
            Assert.Equal(original.WindowCount, clone.WindowCount);
            Assert.Equal(original.IsManualOverride, clone.IsManualOverride);
            Assert.Equal(original.IsProfileEconomyApplied, clone.IsProfileEconomyApplied);
            Assert.Equal(original.Sandwich.Quantity, clone.Sandwich.Quantity);
            Assert.Equal(original.Sandwich.Price, clone.Sandwich.Price);
            Assert.Equal(original.Sandwich.Unit, clone.Sandwich.Unit);
            Assert.Equal(original.Sandwich.Name, clone.Sandwich.Name);
            Assert.Equal(original.Sandwich.IsQuantityOverridden, clone.Sandwich.IsQuantityOverridden);
        }

        [Fact]
        public void DeepClone_ChangesToOriginal_DoNotAffectClone()
        {
            var original = new SlopeCalculation
            {
                WidthMm = 1000,
                HeightMm = 1500,
            };
            original.Foam.Quantity = 2;
            original.Foam.Price = 750;

            var clone = original.DeepClone();
            original.Foam.Quantity = 99;
            original.Foam.Price = 999;

            Assert.Equal(2, clone.Foam.Quantity);
            Assert.Equal(750, clone.Foam.Price);
        }

        [Fact]
        public void DeepClone_AllMaterialsAreCopied()
        {
            var original = new SlopeCalculation();
            SetMaterial(original.Sandwich, 1, 100);
            SetMaterial(original.Foam, 2, 200);
            SetMaterial(original.Sealant, 3, 300);
            SetMaterial(original.Tape, 4, 400);
            SetMaterial(original.StartProfile, 5, 500);
            SetMaterial(original.FProfile, 6, 600);
            SetMaterial(original.Penoplex, 7, 700);
            SetMaterial(original.Laminatina, 8, 800);
            SetMaterial(original.Labor, 9, 900);
            SetMaterial(original.LaminatinaLabor, 10, 1000);

            var clone = original.DeepClone();

            AssertMaterialsEqual(original.Sandwich, clone.Sandwich);
            AssertMaterialsEqual(original.Foam, clone.Foam);
            AssertMaterialsEqual(original.Sealant, clone.Sealant);
            AssertMaterialsEqual(original.Tape, clone.Tape);
            AssertMaterialsEqual(original.StartProfile, clone.StartProfile);
            AssertMaterialsEqual(original.FProfile, clone.FProfile);
            AssertMaterialsEqual(original.Penoplex, clone.Penoplex);
            AssertMaterialsEqual(original.Laminatina, clone.Laminatina);
            AssertMaterialsEqual(original.Labor, clone.Labor);
            AssertMaterialsEqual(original.LaminatinaLabor, clone.LaminatinaLabor);
        }

        [Fact]
        public void DeepClone_Null_ThrowsArgumentNullException()
        {
            SlopeCalculation original = null!;
            Assert.Throws<System.ArgumentNullException>(() => original.DeepClone());
        }

        [Fact]
        public void OrderItem_Clone_UsesSlopeCalculationExtensions()
        {
            var original = new OrderItem
            {
                Name = "Откос",
                SlopeData = new SlopeCalculation
                {
                    WidthMm = 1000,
                    HeightMm = 1500,
                }
            };
            original.SlopeData.Sandwich.Quantity = 1.5;

            var clone = original.Clone();

            Assert.NotSame(original.SlopeData, clone.SlopeData);
            Assert.Equal(1.5, clone.SlopeData!.Sandwich.Quantity);
        }

        private static void SetMaterial(SlopeMaterial material, double quantity, double price)
        {
            material.Quantity = quantity;
            material.Price = price;
        }

        private static void AssertMaterialsEqual(SlopeMaterial expected, SlopeMaterial actual)
        {
            Assert.Equal(expected.Quantity, actual.Quantity);
            Assert.Equal(expected.Price, actual.Price);
            Assert.Equal(expected.Unit, actual.Unit);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.IsQuantityOverridden, actual.IsQuantityOverridden);
        }
    }
}
