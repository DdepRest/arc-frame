using System;
using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Executes an <see cref="AiActionPlan"/> step by step. The caller supplies
    /// a single-command handler (the app's real calculation path) and an
    /// optional rollback action (restore the pre-batch snapshot). The executor
    /// guarantees: validate everything first → execute in order → on any
    /// failure roll the whole batch back → report a per-step result.
    ///
    /// Pure and WPF-free: unit-testable with fake handlers.
    /// </summary>
    public static class AiPlanExecutor
    {
        /// <summary>Handler for one command. Returns success + optional error.</summary>
        public delegate bool CommandHandler(AiCommand command, out string? error);

        public static AiExecutionResult Execute(
            AiActionPlan plan,
            CommandHandler handler,
            Action? rollback = null)
        {
            var validation = AiPlanValidator.Validate(plan);

            if (!validation.IsValid)
            {
                plan.Status = AiPlanStatus.Failed;
                foreach (var s in plan.Steps) s.Status = AiPlanStatus.Failed;
                var failedResult = new AiExecutionResult
                {
                    Success = false,
                    Error = "План не прошёл локальную проверку: "
                            + string.Join(" ", validation.Messages)
                            + string.Join(" ", validation.StepResults.SelectMany(r => r.Messages)),
                    Summary = "Действия не выполнены (ошибка проверки)"
                };
                return failedResult;
            }

            plan.Status = AiPlanStatus.Executing;
            var results = new List<AiStepExecutionResult>();

            for (int i = 0; i < plan.Steps.Count; i++)
            {
                var step = plan.Steps[i];
                step.Status = AiPlanStatus.Executing;

                bool ok;
                string? error;
                try
                {
                    ok = handler(step.ToCommand(), out error);
                }
                catch (Exception ex)
                {
                    ok = false;
                    error = ex.Message;
                }

                results.Add(new AiStepExecutionResult
                {
                    StepId = step.StepId,
                    PreviewText = step.PreviewText,
                    Success = ok,
                    Error = error
                });

                if (!ok)
                {
                    step.Status = AiPlanStatus.Failed;
                    if (rollback != null)
                    {
                        try { rollback(); } catch { /* best-effort rollback */ }
                        plan.Status = AiPlanStatus.RolledBack;
                        // Steps after the failed one never started — mark them
                        // rolled back so the plan card tells a consistent story.
                        foreach (var s in plan.Steps)
                            if (s.Status is not (AiPlanStatus.Executed or AiPlanStatus.Failed or AiPlanStatus.RolledBack))
                                s.Status = AiPlanStatus.RolledBack;
                        var rolledBack = new AiExecutionResult
                        {
                            Success = false,
                            RolledBack = true,
                            Error = error ?? "Неизвестная ошибка выполнения",
                            Summary = $"Ошибка на шаге {i + 1}. Все изменения откачены."
                        };
                        rolledBack.StepResults.AddRange(results);
                        return rolledBack;
                    }

                    plan.Status = AiPlanStatus.Failed;
                    var stepFailed = new AiExecutionResult
                    {
                        Success = false,
                        Error = error ?? "Неизвестная ошибка выполнения",
                        Summary = $"Ошибка на шаге {i + 1} из {plan.Steps.Count}."
                    };
                    stepFailed.StepResults.AddRange(results);
                    return stepFailed;
                }

                step.Status = AiPlanStatus.Executed;
            }

            plan.Status = AiPlanStatus.Executed;
            plan.ExecutedAt = DateTime.Now;
            var successResult = new AiExecutionResult
            {
                Success = true,
                Summary = $"Выполнено {results.Count(r => r.Success)} из {plan.Steps.Count} действий."
            };
            successResult.StepResults.AddRange(results);
            return successResult;
        }
    }
}
