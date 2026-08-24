using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    public sealed class AiResponse
    {
        public string Reply { get; init; } = "";

        /// <summary>Legacy single-action result (kept for backward compatibility).</summary>
        public AiCommand? Action { get; init; }

        /// <summary>
        /// Plan-mode result (mode/steps JSON). When present the caller should
        /// route the reply through the plan → preview → confirm → execute
        /// pipeline instead of executing <see cref="Action"/> directly.
        /// </summary>
        public AiActionPlan? Plan { get; init; }

        /// <summary>
        /// High-level mode the model signalled (answer / clarification / plan /
        /// explanation). Defaults to <see cref="AiPlanMode.Answer"/>; validation
        /// overrides that ask the user for missing data are marked as
        /// clarification so the UI can attach the interactive parameter form.
        /// </summary>
        public AiPlanMode Mode { get; init; } = AiPlanMode.Answer;
    }

    public sealed class AiCommand
    {
        public AiCommandType Type { get; init; }
        public AiCommandParams Params { get; init; } = new();
    }

    public enum AiCommandType
    {
        AddItem, DeleteLast, ClearAll, ListProducts, CalcSlope, UpdateItems, DeleteItems
    }

    public sealed class AiCommandParams
    {
        public string Type { get; init; } = "";
        public string Color { get; init; } = "";
        public int Width { get; init; }
        public int Height { get; init; }
        public int Depth { get; init; }
        public double Quantity { get; init; } = 1;
        public double Price { get; init; }
        public AnwisSizeMode AnwisMode { get; init; } = AnwisSizeService.DefaultMode;
        public int InstallationMode { get; init; } = -1;
        /// <summary>True when this add originates from the «Свой товар» flow (user-entered name).</summary>
        public bool IsCustomProduct { get; init; }

        public string TargetProduct { get; init; } = "";
        public int? UpdateInstallationMode { get; init; }
        public double? UpdatePrice { get; init; }
        public double? UpdateInstallationAmount { get; init; }
        public AnwisSizeMode? UpdateAnwisMode { get; init; }
        public string? UpdateColor { get; init; }
    }
}
