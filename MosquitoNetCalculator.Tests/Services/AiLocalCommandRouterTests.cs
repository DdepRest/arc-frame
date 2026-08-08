using System.Collections.Generic;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class AiLocalCommandRouterTests
    {
        private static AiOrderContext EmptyOrder() => new() { Count = 0, Total = 0 };

        [Fact]
        public void PlainText_IsNotHandled()
        {
            var r = AiLocalCommandRouter.TryRoute("Сделай сетку 700×1400", EmptyOrder());

            Assert.False(r.IsHandled);
        }

        [Fact]
        public void UnknownCommand_ShowsHelp_AndIsHandled()
        {
            var r = AiLocalCommandRouter.TryRoute("/xyzzy", EmptyOrder());

            Assert.True(r.IsHandled);
            Assert.Equal(RouteKind.Info, r.Kind);
            Assert.Contains("Неизвестная команда", r.Message);
            Assert.Contains("/товары", r.Message);
        }

        [Fact]
        public void Products_ReturnsCatalog_WithoutOrder()
        {
            var r = AiLocalCommandRouter.TryRoute("/товары", null);

            Assert.True(r.IsHandled);
            Assert.Equal(RouteKind.Info, r.Kind);
            Assert.Contains("Anwis", r.Message);
            Assert.Contains("Отлив", r.Message);
        }

        [Fact]
        public void Prices_EnglishAlias_Works()
        {
            var r = AiLocalCommandRouter.TryRoute("/prices", null);

            Assert.True(r.IsHandled);
            Assert.Contains("Anwis", r.Message);
            Assert.Contains("₽/м²", r.Message); // prices are local and formatted
        }

        [Fact]
        public void Totals_EmptyOrder_ExplainsHowToStart()
        {
            var r = AiLocalCommandRouter.TryRoute("/итоги", EmptyOrder());

            Assert.True(r.IsHandled);
            Assert.Contains("Расчёт пуст", r.Message);
        }

        [Fact]
        public void Totals_WithOrder_ShowsBriefAndCategories()
        {
            var order = new AiOrderContext
            {
                Count = 1,
                Total = 1800,
                ItemsTotal = 1800,
                TotalArea = 1.0
            };
            order.Items.Add(new AiOrderItemInfo
            {
                Index = 1, Name = "Anwis", Color = "Белый", Category = "сетки",
                WidthCalc = 700, HeightCalc = 1400,
                Quantity = 1, Price = 1800, TotalWithInstall = 1800,
                IsAreaBased = true, IsActive = true
            });

            var r = AiLocalCommandRouter.TryRoute("/итоги", order);

            Assert.True(r.IsHandled);
            Assert.Contains("Позиций: 1", r.Message);
            Assert.Contains("1 800", r.Message); // MoneyFormatService uses ru grouping
        }

        [Fact]
        public void Undo_And_Redo_Kinds()
        {
            Assert.Equal(RouteKind.Undo, AiLocalCommandRouter.TryRoute("/отменить", EmptyOrder()).Kind);
            Assert.Equal(RouteKind.Undo, AiLocalCommandRouter.TryRoute("/undo", EmptyOrder()).Kind);
            Assert.Equal(RouteKind.Redo, AiLocalCommandRouter.TryRoute("/повторить", EmptyOrder()).Kind);
            Assert.Equal(RouteKind.Redo, AiLocalCommandRouter.TryRoute("/redo", EmptyOrder()).Kind);
        }

        [Fact]
        public void Clear_ProducesClearAllCommand()
        {
            var r = AiLocalCommandRouter.TryRoute("/очистить", new AiOrderContext { Count = 3, Total = 4500 });

            Assert.True(r.IsHandled);
            Assert.Equal(RouteKind.ClearPlan, r.Kind);
            Assert.Single(r.Commands);
            Assert.Equal(AiCommandType.ClearAll, r.Commands[0].Type);
            Assert.Contains("Будет удалено 3 позиций", r.Message);
        }

        [Fact]
        public void Explain_Last_IsDefaultTarget()
        {
            var r = AiLocalCommandRouter.TryRoute("/объясни", EmptyOrder());

            Assert.Equal(RouteKind.Explain, r.Kind);
            Assert.Equal(ExplainTarget.Last, r.ExplainTarget);
        }

        [Fact]
        public void Explain_Position_ExtractsIndex()
        {
            var r = AiLocalCommandRouter.TryRoute("/объясни позицию 3", EmptyOrder());

            Assert.Equal(RouteKind.Explain, r.Kind);
            Assert.Equal(ExplainTarget.Index, r.ExplainTarget);
            Assert.Equal(3, r.ExplainIndex);
        }

        [Fact]
        public void Explain_All_Target()
        {
            var r = AiLocalCommandRouter.TryRoute("/explain всё", EmptyOrder());

            Assert.Equal(RouteKind.Explain, r.Kind);
            Assert.Equal(ExplainTarget.All, r.ExplainTarget);
        }

        [Fact]
        public void Status_IncludesModelAndSession()
        {
            var session = new AiSessionSummary
            {
                Requests = 4, Succeeded = 3, Fallbacks = 1,
                ModelsUsed = new[] { "NVIDIA · Nemotron" },
                LastModel = "NVIDIA · Nemotron"
            };

            var r = AiLocalCommandRouter.TryRoute("/статус", EmptyOrder(), currentModel: "NVIDIA · Nemotron", session: session);

            Assert.True(r.IsHandled);
            Assert.Contains("Модель: NVIDIA · Nemotron", r.Message);
            Assert.Contains("Запросов: 4", r.Message);
        }

        [Fact]
        public void Help_Command_ListsAllCommands()
        {
            var r = AiLocalCommandRouter.TryRoute("/помощь", null);

            Assert.True(r.IsHandled);
            Assert.Contains("/объясни", r.Message);
            Assert.Contains("/очистить", r.Message);
        }

        [Fact]
        public void CommandCatalog_ContainsAllCommandsWithDescriptions()
        {
            var commands = AiLocalCommandRouter.Commands;

            Assert.NotEmpty(commands);
            // Every catalog entry is also routable.
            foreach (var c in commands)
            {
                Assert.False(string.IsNullOrWhiteSpace(c.Command));
                Assert.False(string.IsNullOrWhiteSpace(c.Description));
                var main = c.Command.Split(' ')[0];
                Assert.True(AiLocalCommandRouter.TryRoute(main, EmptyOrder()).IsHandled,
                    $"Command '{main}' from the catalog is not routable.");
            }

            Assert.Contains(commands, c => c.Command.StartsWith("/товары"));
            Assert.Contains(commands, c => c.Command.StartsWith("/итоги"));
            Assert.Contains(commands, c => c.Command.StartsWith("/объясни"));
            Assert.Contains(commands, c => c.Command.StartsWith("/очистить"));
        }

        [Fact]
        public void CommandCatalog_MatchWorksForPartialPrefixAndAliases()
        {
            var all = AiLocalCommandRouter.Commands;

            // «/ит» matches /итоги by prefix.
            Assert.Contains(all, c => c.Matches("/ит"));
            // «/pr» matches the /products alias of /товары.
            Assert.Contains(all, c => c.Matches("/pr") && c.Aliases.Contains("/products"));
            // Empty query matches everything (shows all commands).
            Assert.All(all, c => Assert.True(c.Matches("/")));
            // Unrelated query matches nothing.
            Assert.DoesNotContain(all, c => c.Matches("/zzz"));
        }
    }
}
