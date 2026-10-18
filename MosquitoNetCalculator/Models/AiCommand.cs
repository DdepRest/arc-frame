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
        AddItem,
        DeleteLast,
        ClearAll,
        ListProducts,
        CalcSlope,
        UpdateItems,
        DeleteItems,
        UpdateItemByIndex,
        DeleteItemByIndex,
        ToggleItemActive,
        SetClientInfo,
        ApplyTemplate,
        SaveOrder,
        PrintKp,
        ExportPdf
    }

    public sealed class AiCommandParams
    {
        public string Type { get; set; } = "";
        public string Color { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public int Depth { get; set; }
        public double Quantity { get; set; } = 1;
        public double Price { get; set; }
        public AnwisSizeMode AnwisMode { get; set; } = AnwisSizeService.DefaultMode;
        public int InstallationMode { get; set; } = -1;
        /// <summary>True when this add originates from the «Свой товар» flow (user-entered name).</summary>
        public bool IsCustomProduct { get; set; }
        /// <summary>Флаг опции сетки «Антикошка».</summary>
        public bool? IsAnticat { get; set; }

        public string TargetProduct { get; init; } = "";
        public int? UpdateInstallationMode { get; init; }
        public double? UpdatePrice { get; init; }
        public double? UpdateInstallationAmount { get; init; }
        public AnwisSizeMode? UpdateAnwisMode { get; init; }
        public string? UpdateColor { get; init; }

        /// <summary>Номер целевой строки в таблице заказа (1-based).</summary>
        public int ItemIndex { get; init; }
        /// <summary>Новая ширина при точечном обновлении строки.</summary>
        public int? UpdateWidth { get; init; }
        /// <summary>Новая высота при точечном обновлении строки.</summary>
        public int? UpdateHeight { get; init; }
        /// <summary>Новое количество при точечном обновлении строки.</summary>
        public double? UpdateQuantity { get; init; }
        /// <summary>Флаг активности строки (включена ли в расчёт).</summary>
        public bool? IsActive { get; init; }

        /// <summary>Идентификатор шаблона заказа («window», «balcony», «frame», «french»).</summary>
        public string TemplateId { get; init; } = "";

        // Данные клиента
        public string ClientPhone { get; init; } = "";
        public string ClientAddress { get; init; } = "";
        public string ContractNumber { get; init; } = "";
        public string Notes { get; init; } = "";
    }
}
