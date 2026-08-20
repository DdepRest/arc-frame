using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Centralised «don't invent» safety policy for AI-generated
    /// <see cref="AiActionPlan"/>s. Every code path that builds a plan
    /// (LLM plan-mode, legacy single-action, clarification form submit,
    /// slash command router) must run <see cref="NeedsClarification"/>
    /// on the resulting commands before showing the preview / executing.
    ///
    /// The policy is a local deterministic check; it does NOT look at
    /// the LLM reply and never trusts the model on its own. Public
    /// intent classifier <see cref="AiClarificationForm.ShouldAskForMissingParams"/>
    /// is reused here so the form and the validator agree on what
    /// «unsafe» means. The policy is a strict superset: it adds the
    /// update-target guard the form doesn't carry.
    /// </summary>
    public static class AiPlanSafetyPolicy
    {
        /// <summary>
        /// Identifies the first failing rule. Order is priority — Anwis
        /// mode > dimensions > монтаж > update-target — so the UI can
        /// show one specific message above the clarification card.
        /// </summary>
        public enum MissingField
        {
            None,
            AnwisMode,
            Dimensions,
            InstallationMode,
            UpdateTarget
        }

        /// <summary>
        /// True when at least one command would invent critical data the
        /// user never supplied (Anwis mode, dimensions for sized products,
        /// монтаж for installation-applicable products, untargeted update).
        /// </summary>
        public static bool NeedsClarification(IReadOnlyList<AiCommand>? commands, string? sourceUserText)
            => Classify(commands, sourceUserText) != MissingField.None;

        /// <summary>
        /// Returns the first failing guard. <see cref="MissingField.None"/>
        /// means the plan is safe to preview / execute as-is.
        /// </summary>
        public static MissingField Classify(IReadOnlyList<AiCommand>? commands, string? sourceUserText)
        {
            if (commands == null || commands.Count == 0) return MissingField.None;

            foreach (var c in commands)
            {
                if (c == null) continue;
                if (c.Type == AiCommandType.AddItem)
                {
                    if (IsMissingAnwisMode(c, sourceUserText)) return MissingField.AnwisMode;
                    if (IsMissingDimensions(c)) return MissingField.Dimensions;
                    if (IsMissingInstallation(c, sourceUserText)) return MissingField.InstallationMode;
                    continue;
                }
                if (c.Type == AiCommandType.UpdateItems && IsUntargetedUpdate(c))
                    return MissingField.UpdateTarget;
            }
            return MissingField.None;
        }

        /// <summary>
        /// Human-friendly snippet for the missing field, used both by the
        /// chat bubble above the form and by tests as a stable identifier.
        /// Order matches <see cref="Classify"/>.
        /// </summary>
        public static string MissingReasonText(MissingField field) => field switch
        {
            MissingField.AnwisMode => "Не указан режим Anwis — выберите профиль в карточке ниже:",
            MissingField.Dimensions => "Не хватает параметров — заполните карточку ниже:",
            MissingField.InstallationMode => "Не указан монтаж — выберите вариант в карточке ниже:",
            MissingField.UpdateTarget => "Не указано, к каким позициям применить изменение — выберите в карточке ниже:",
            _ => ""
        };

        // ── Per-rule guard predicates (also reachable by tests) ─────────

        /// <summary>Anwis product without a user-named size mode is unsafe.</summary>
        public static bool IsMissingAnwisMode(AiCommand c, string? userText)
            => c.Type == AiCommandType.AddItem
                && AnwisSizeService.IsApplicable(c.Params.Type)
                && !AiClarificationForm.AnwisModeSpecified(userText);

        /// <summary>Sized (non-manual) product without width/height is unsafe.</summary>
        public static bool IsMissingDimensions(AiCommand c)
            => c.Type == AiCommandType.AddItem
                && !ProductCatalog.IsManualPiece(c.Params.Type)
                && (c.Params.Width <= 0 || c.Params.Height <= 0);

        /// <summary>Installation-applicable product without a user-named mode is unsafe.</summary>
        public static bool IsMissingInstallation(AiCommand c, string? userText)
            => c.Type == AiCommandType.AddItem
                && ProductCatalog.IsInstallationApplicable(c.Params.Type)
                && !AiClarificationForm.InstallationModeSpecified(userText);

        /// <summary>
        /// Updates without a target category or product name are unsafe:
        /// «сделай всё белым» would have to touch every position. The user
        /// must name the target explicitly.
        /// </summary>
        public static bool IsUntargetedUpdate(AiCommand c)
            => c.Type == AiCommandType.UpdateItems
                && string.IsNullOrWhiteSpace(c.Params.TargetProduct);
    }
}
