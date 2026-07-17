using System;
using System.Collections.Generic;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Tests for the pure parts of <see cref="OrderImportExportService"/> —
    /// <c>DeepCloneOrder</c> (JSON round-trip) and <c>BuildSingleOrderFileName</c>
    /// (string composition). UI-bound methods (Export/Import dialogs) are
    /// covered indirectly via the VM/storage integration tests; they are
    /// excluded here to avoid STA-host coupling.
    /// </summary>
    public class OrderImportExportServiceTests
    {
        // ─── DeepCloneOrder ─────────────────────────────────

        [Fact]
        public void DeepCloneOrder_ReturnsIndependentReference()
        {
            var source = new OrderData { Id = "src", ContractNumber = "1-1", ClientName = "Source" };

            var copy = OrderImportExportService.DeepCloneOrder(source);

            Assert.NotNull(copy);
            Assert.NotSame(source, copy);                     // different object
            Assert.Equal("src", copy!.Id);                    // data preserved
            Assert.Equal("1-1", copy.ContractNumber);
            Assert.Equal("Source", copy.ClientName);
        }

        [Fact]
        public void DeepCloneOrder_NullSource_ReturnsNull()
        {
            Assert.Null(OrderImportExportService.DeepCloneOrder((OrderData?)null));
        }

        [Fact]
        public void DeepCloneOrder_PreservesAllFieldKindsIncludingIsAnticat()
        {
            var source = new OrderData
            {
                Id = "x",
                ContractNumber = "1-1",
                ClientName = "Иванов",
                ClientAddress = "ул. Ленина, д. 5",
                Notes = "Позвонить за час",
                HasAdditionalKp = true,
                AdditionalKps = new List<AdditionalKpItem>
                {
                    new() { Number = "3-1", Amount = 500, IsActive = true }
                },
                Items = new List<OrderItemData>
                {
                    new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 2, Price = 1800, IsAnticat = true, AnwisSizeMode = 0, IsActive = true },
                    new() { Name = "Работа", Quantity = 1, Price = 5000, IsActive = true }
                }
            };

            var copy = OrderImportExportService.DeepCloneOrder(source);

            Assert.NotNull(copy);
            Assert.Equal("Иванов", copy!.ClientName);
            Assert.Equal("ул. Ленина, д. 5", copy.ClientAddress);
            Assert.Equal("Позвонить за час", copy.Notes);
            Assert.True(copy.HasAdditionalKp);
            Assert.Single(copy.AdditionalKps);
            Assert.Equal(500, copy.AdditionalKps[0].Amount);
            Assert.Equal("3-1", copy.AdditionalKps[0].Number);
            Assert.Equal(2, copy.Items.Count);
            Assert.Equal("Anwis", copy.Items[0].Name);
            Assert.True(copy.Items[0].IsAnticat);
            Assert.Equal("Работа", copy.Items[1].Name);
        }

        [Fact]
        public void DeepCloneOrder_DoesNotMutateSource()
        {
            // JSON round-trip must produce a copy the caller can mutate
            // (Id, ContractNumber, Status, CreatedAt, UpdatedAt) without
            // affecting the source.
            var source = new OrderData { Id = "src", ContractNumber = "1-1" };

            var copy = OrderImportExportService.DeepCloneOrder(source);
            copy!.Id = "new";
            copy.ContractNumber = "1-2";

            Assert.Equal("src", source.Id);                  // untouched
            Assert.Equal("1-1", source.ContractNumber);
        }

        // ─── BuildSingleOrderFileName ───────────────────────

        [Fact]
        public void BuildSingleOrderFileName_EmptyAddress_FallsBackToContractNumber()
        {
            var order = new OrderData { ClientAddress = "", ContractNumber = "1-5" };
            Assert.Equal("order 1-5.json", OrderImportExportService.BuildSingleOrderFileName(order));
        }

        [Fact]
        public void BuildSingleOrderFileName_NullAddress_FallsBackToContractNumber()
        {
            // OrderData.ClientAddress is `string` (non-nullable). The
            // null-forgiving operator is necessary here because the
            // production code path under test (sanitize + uppercase)
            // must gracefully handle null address input. We pin the
            // contract via `null!` rather than refactoring OrderData's
            // JSON contract.
            var order = new OrderData { ClientAddress = null!, ContractNumber = "2-3" };
            Assert.Equal("order 2-3.json", OrderImportExportService.BuildSingleOrderFileName(order));
        }

        [Fact]
        public void BuildSingleOrderFileName_WhitespaceOnlyAddress_FallsBackToContractNumber()
        {
            var order = new OrderData { ClientAddress = "   ", ContractNumber = "3-1" };
            Assert.Equal("order 3-1.json", OrderImportExportService.BuildSingleOrderFileName(order));
        }

        [Fact]
        public void BuildSingleOrderFileName_ReplacesSlashesWithSpaces()
        {
            var order = new OrderData { ClientAddress = "ул. Ленина/д.5", ContractNumber = "1-1" };
            var name = OrderImportExportService.BuildSingleOrderFileName(order);
            Assert.Equal("УЛ. ЛЕНИНА Д.5 1-1.json", name);
        }

        [Fact]
        public void BuildSingleOrderFileName_UppercasesAndThrowsAwayExtraSpaces()
        {
            var order = new OrderData { ClientAddress = "  ул.  Ленина   5  ", ContractNumber = "2-8" };
            var name = OrderImportExportService.BuildSingleOrderFileName(order);
            Assert.Equal("УЛ. ЛЕНИНА 5 2-8.json", name);
        }

        [Fact]
        public void BuildSingleOrderFileName_StripsInvalidFileNameChars()
        {
            // Quotes, angle brackets, pipes, asterisks and colons are all
            // invalid on Windows. Question mark is sometimes accepted by
            // other platforms; deliberately NOT in the test input.
            var order = new OrderData
            {
                ClientAddress = "ул. \"<>|*Ленина",
                ContractNumber = "4-2"
            };
            var name = OrderImportExportService.BuildSingleOrderFileName(order);
            Assert.Equal("УЛ. ЛЕНИНА 4-2.json", name);
        }

        [Fact]
        public void BuildSingleOrderFileName_TrimTo60Chars()
        {
            var longAddress = new string('A', 100);
            var order = new OrderData { ClientAddress = longAddress, ContractNumber = "1-1" };
            var name = OrderImportExportService.BuildSingleOrderFileName(order);
            // "A..A" (60 chars) + " " + "1-1" + ".json" — address portion capped at 60.
            Assert.Contains(" 1-1.json", name);
            Assert.True(name.Length - " 1-1.json".Length <= 60,
                $"Address portion exceeded 60 chars: name='{name}' (len={name.Length})");
        }

        [Fact]
        public void BuildSingleOrderFileName_NullOrder_ThrowsCleanly()
        {
            // Contract: documented as defensive — null in → empty string out.
            Assert.Equal(string.Empty, OrderImportExportService.BuildSingleOrderFileName((OrderData?)null));
        }

        [Fact]
        public void CopyOrderIdentityResetMirrorsOldInline_CallerOwned()
        {
            // The contract of DeepCloneOrder is "identity-preserving copy,
            // caller assigns new Identity". This test pins the invariant
            // that DeepCloneOrder itself does NOT touch Id / ContractNumber /
            // Status / CreatedAt / UpdatedAt — caller code (CopyOrder) does.
            var source = new OrderData { Id = "src", ContractNumber = "1-1", Status = "Оплачен" };

            var clone = OrderImportExportService.DeepCloneOrder(source);

            Assert.Equal("src", clone!.Id);
            Assert.Equal("1-1", clone.ContractNumber);
            Assert.Equal("Оплачен", clone.Status);
        }
    }
}
