using MosquitoNetCalculator.Models;
using Xunit;

namespace MosquitoNetCalculator.Tests.Models
{
    public class ClientInfoTests
    {
        [Fact]
        public void AdditionalKpsTotal_Zero_WhenHasAdditionalKpFalse()
        {
            var info = new ClientInfo();
            // HasAdditionalKp is false by default.
            // Note: adding items to AdditionalKps auto-syncs HasAdditionalKp to true
            // via CollectionChanged. So we test the property directly without adding items.
            Assert.False(info.HasAdditionalKp);
            Assert.Equal(0, info.AdditionalKpsTotal);
        }

        [Fact]
        public void AdditionalKpsTotal_SumsActiveItems()
        {
            var info = new ClientInfo { HasAdditionalKp = true };
            info.AdditionalKps.Add(new AdditionalKpItem { Amount = 500, IsActive = true });
            info.AdditionalKps.Add(new AdditionalKpItem { Amount = 300, IsActive = true });
            Assert.Equal(800, info.AdditionalKpsTotal);
        }

        [Fact]
        public void AdditionalKpsTotal_IgnoresInactiveItems()
        {
            var info = new ClientInfo { HasAdditionalKp = true };
            info.AdditionalKps.Add(new AdditionalKpItem { Amount = 500, IsActive = true });
            info.AdditionalKps.Add(new AdditionalKpItem { Amount = 300, IsActive = false });
            Assert.Equal(500, info.AdditionalKpsTotal);
        }

        [Fact]
        public void HasAdditionalKp_AutoAddsItem_WhenSetTrueAndEmpty()
        {
            var info = new ClientInfo();
            Assert.Empty(info.AdditionalKps);
            info.HasAdditionalKp = true;
            Assert.Single(info.AdditionalKps);
        }

        [Fact]
        public void HasAdditionalKp_DoesNotAddItem_WhenSetTrueAndNotEmpty()
        {
            var info = new ClientInfo();
            info.AdditionalKps.Add(new AdditionalKpItem());
            info.HasAdditionalKp = true;
            Assert.Single(info.AdditionalKps);
        }

        [Fact]
        public void PropertyChanged_Fired_OnClientNameChange()
        {
            var info = new ClientInfo();
            string? changed = null;
            info.PropertyChanged += (s, e) => changed = e.PropertyName;
            info.ClientName = "Test";
            Assert.Equal("ClientName", changed);
        }

        [Fact]
        public void PropertyChanged_NotFired_WhenSameValue()
        {
            var info = new ClientInfo { ClientName = "Test" };
            int count = 0;
            info.PropertyChanged += (s, e) => count++;
            info.ClientName = "Test";
            Assert.Equal(0, count);
        }

        [Fact]
        public void AdditionalKpsTotal_UpdatesWhenItemAmountChanges()
        {
            var info = new ClientInfo { HasAdditionalKp = true };
            var kp = new AdditionalKpItem { Amount = 100, IsActive = true };
            info.AdditionalKps.Add(kp);
            Assert.Equal(100, info.AdditionalKpsTotal);

            kp.Amount = 500;
            Assert.Equal(500, info.AdditionalKpsTotal);
        }

        [Fact]
        public void AdditionalKpsTotal_UpdatesWhenItemBecomesInactive()
        {
            var info = new ClientInfo { HasAdditionalKp = true };
            var kp = new AdditionalKpItem { Amount = 500, IsActive = true };
            info.AdditionalKps.Add(kp);
            Assert.Equal(500, info.AdditionalKpsTotal);

            kp.IsActive = false;
            Assert.Equal(0, info.AdditionalKpsTotal);
        }

        [Fact]
        public void HasAdditionalKp_PreservesData_WhenSetFalse()
        {
            // Setting HasAdditionalKp=true auto-adds an empty item when list is empty
            var info = new ClientInfo { HasAdditionalKp = true };
            // Now add our test item
            info.AdditionalKps.Add(new AdditionalKpItem { Number = "2-1", Amount = 500 });
            int countBefore = info.AdditionalKps.Count; // 2 (auto-added + ours)

            info.HasAdditionalKp = false;

            // Data should be preserved even when hidden — count unchanged
            Assert.Equal(countBefore, info.AdditionalKps.Count);
            Assert.Contains(info.AdditionalKps, kp => kp.Number == "2-1" && kp.Amount == 500);
        }

        [Fact]
        public void DefaultPropertyValues()
        {
            var info = new ClientInfo();
            Assert.Equal("", info.ClientName);
            Assert.Equal("", info.ClientPhone);
            Assert.Equal("", info.ClientAddress);
            Assert.Equal("", info.ContractNumber);
            Assert.Equal("", info.Notes);
            Assert.False(info.HasAdditionalKp);
            Assert.Equal(System.DateTime.Today, info.ContractDate);
        }

        [Fact]
        public void PropertyChanged_Fired_OnNotesChange()
        {
            var info = new ClientInfo();
            string? changed = null;
            info.PropertyChanged += (s, e) => changed = e.PropertyName;
            info.Notes = "Заметка";
            Assert.Equal("Notes", changed);
        }

        [Fact]
        public void PropertyChanged_Fired_OnContractDateChange()
        {
            var info = new ClientInfo();
            string? changed = null;
            info.PropertyChanged += (s, e) => changed = e.PropertyName;
            info.ContractDate = new System.DateTime(2025, 1, 1);
            Assert.Equal("ContractDate", changed);
        }
    }
}
