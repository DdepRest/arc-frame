using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class AiPlanExecutorTests
    {
        private static AiActionPlan TwoStepPlan()
            => AiPlanBuilder.FromCommands(new[]
            {
                new AiCommand
                {
                    Type = AiCommandType.AddItem,
                    Params = new AiCommandParams { Type = "Anwis", Color = "Белый", Width = 700, Height = 1400, Quantity = 1 }
                },
                new AiCommand
                {
                    Type = AiCommandType.AddItem,
                    Params = new AiCommandParams { Type = "Отлив", Color = "Коричневый", Width = 200, Height = 1500, Quantity = 2 }
                }
            }, "добавь две позиции");

        [Fact]
        public void Execute_Success_RunsAllStepsInOrder()
        {
            var plan = TwoStepPlan();
            var executed = new List<AiCommandType>();

            var result = AiPlanExecutor.Execute(plan, (AiCommand cmd, out string? err) =>
            {
                err = null;
                executed.Add(cmd.Type);
                return true;
            });

            Assert.True(result.Success);
            Assert.False(result.RolledBack);
            Assert.Equal(new[] { AiCommandType.AddItem, AiCommandType.AddItem }, executed);
            Assert.Equal(AiPlanStatus.Executed, plan.Status);
            Assert.NotNull(plan.ExecutedAt);
            Assert.Contains("Выполнено 2 из 2", result.Summary);
        }

        [Fact]
        public void Execute_Failure_RollsBackWholeBatch()
        {
            var plan = TwoStepPlan();
            bool rollbackCalled = false;
            int attempts = 0;

            var result = AiPlanExecutor.Execute(plan, (AiCommand cmd, out string? err) =>
            {
                attempts++;
                if (cmd.Params.Type == "Отлив")
                {
                    err = "модель забыла цену";
                    return false;
                }
                err = null;
                return true;
            }, rollback: () => rollbackCalled = true);

            Assert.False(result.Success);
            Assert.True(result.RolledBack);
            Assert.True(rollbackCalled);
            Assert.Equal(AiPlanStatus.RolledBack, plan.Status);
            // Step 1 succeeded, step 2 failed → 2 handler invocations, then stop.
            Assert.Equal(2, attempts);
            Assert.Equal(AiPlanStatus.Executed, plan.Steps[0].Status);
            Assert.Equal(AiPlanStatus.Failed, plan.Steps[1].Status);
            Assert.Contains("откачены", result.Summary);
        }

        [Fact]
        public void Execute_Failure_WithoutRollback_ReportsFailedAndStops()
        {
            var plan = TwoStepPlan();
            int attempts = 0;

            var result = AiPlanExecutor.Execute(plan, (AiCommand cmd, out string? err) =>
            {
                attempts++;
                if (cmd.Params.Type == "Отлив")
                {
                    err = "сбой";
                    return false;
                }
                err = null;
                return true;
            });

            Assert.False(result.Success);
            Assert.False(result.RolledBack);
            Assert.Equal(2, attempts); // never reaches step 3 (there is none) — stops at step 2
            Assert.Equal(AiPlanStatus.Failed, plan.Status);
        }

        [Fact]
        public void Execute_InvalidPlan_NeverCallsHandler()
        {
            var plan = AiPlanBuilder.FromCommand(new AiCommand
            {
                Type = AiCommandType.AddItem,
                Params = new AiCommandParams { Type = "Несуществующий", Width = 0, Height = 0 }
            });

            bool handlerCalled = false;

            var result = AiPlanExecutor.Execute(plan, (AiCommand _, out string? err) =>
            {
                handlerCalled = true;
                err = null;
                return true;
            });

            Assert.False(result.Success);
            Assert.False(handlerCalled);
            Assert.Equal(AiPlanStatus.Failed, plan.Status);
            Assert.Contains("проверку", result.Error);
        }

        [Fact]
        public void Execute_HandlerException_IsCapturedAndRollsBack()
        {
            var plan = TwoStepPlan();
            bool rolledBack = false;

            var result = AiPlanExecutor.Execute(plan, (AiCommand cmd, out string? err) =>
            {
                if (cmd.Params.Type == "Отлив") throw new System.InvalidOperationException("взорвалось");
                err = null;
                return true;
            }, rollback: () => rolledBack = true);

            Assert.False(result.Success);
            Assert.True(result.RolledBack);
            Assert.True(rolledBack);
            Assert.Contains("взорвалось", result.Error);
        }

        [Fact]
        public void Execute_Success_ReportsPerStepResults()
        {
            var plan = TwoStepPlan();

            var result = AiPlanExecutor.Execute(plan, (AiCommand _, out string? err) => { err = null; return true; });

            Assert.Equal(2, result.StepResults.Count);
            Assert.All(result.StepResults, r => Assert.True(r.Success));
            Assert.All(result.StepResults, r => Assert.False(string.IsNullOrEmpty(r.PreviewText)));
        }
    }
}
