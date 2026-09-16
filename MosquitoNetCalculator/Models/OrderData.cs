using System;
using System.Collections.Generic;

namespace MosquitoNetCalculator.Models
{
    public class OrderData
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public string Status { get; set; } = OrderStatuses.All[0];

        public string ContractNumber { get; set; } = "";
        public DateTime ContractDate { get; set; } = DateTime.Today;
        public string ClientName { get; set; } = "";
        public string ClientPhone { get; set; } = "";
        public string ClientAddress { get; set; } = "";
        public string Notes { get; set; } = "";
        public bool HasAdditionalKp { get; set; }
        public string AdditionalKpNumber { get; set; } = "";
        public double AdditionalKpAmount { get; set; }
        /// <summary>
        /// List of additional КП entries (v3.9+). Each has a Number and Amount.
        /// For backward compat, old orders with single AdditionalKpNumber/AdditionalKpAmount
        /// are migrated into this list on load.
        /// </summary>
        public List<AdditionalKpItem> AdditionalKps { get; set; } = new();
        public double TotalAmount { get; set; }
        public List<OrderItemData> Items { get; set; } = new();

        /// <summary>
        /// Workflow rank of the current Status — exposed via the property
        /// so the Orders grid's «Статус» column can SortMemberPath it
        /// and sort by lifecycle progress instead of Cyrillic order.
        /// Read on every sort comparison; OrderData is the DTO loaded
        /// from JSON, no INPC required (a fresh LoadAllOrders reads the
        /// current value).
        /// </summary>
        /// <remarks>
        /// Marked <c>[JsonIgnore]</c> so the dead computed value isn't
        /// written into saved JSON. System.Text.Json default-includes
        /// read-only properties in the output — without JsonIgnore every
        /// saved order would carry a ~30-byte «StatusRank» field that
        /// the deserializer immediately throws away on load (no setter).
        /// That violates the v3.22+ "no dead bytes in JSON" convention
        /// pinned by the <c>SaveOrder_DoesNotWriteRemovedFields_ToJson</c>
        /// regression test.
        /// </remarks>
        [System.Text.Json.Serialization.JsonIgnore]
        public int StatusRank => OrderStatuses.GetRank(Status);

        // Natural-sort key for the «№ КП» column in the Orders grid.
        // WPF DataGrid sort is lexicographic by default — without this key the
        // column sorts «2-1, 2-10, 2-2, …» instead of the natural «2-1, 2-2, …, 2-9,
        // 2-10». We pre-pad every numeric run with 10 zeros so the default string
        // comparator produces correct numeric ordering. Cached: the key is
        // recomputed only when <see cref="ContractNumber"/> changes, otherwise
        // the same StringBuilder allocation is reused across re-sort / refresh.
        private string? _contractNumberForSort;
        private string? _contractNumberSortKeyCache;

        [System.Text.Json.Serialization.JsonIgnore]
        public string ContractNumberSortKey
        {
            get
            {
                if (_contractNumberForSort != ContractNumber)
                {
                    _contractNumberForSort = ContractNumber;
                    _contractNumberSortKeyCache = BuildNaturalSortKey(ContractNumber);
                }
                return _contractNumberSortKeyCache ?? string.Empty;
            }
        }

        private static string BuildNaturalSortKey(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var sb = new System.Text.StringBuilder(value.Length + 12);
            int i = 0;
            while (i < value.Length)
            {
                if (char.IsDigit(value[i]))
                {
                    int start = i;
                    while (i < value.Length && char.IsDigit(value[i])) i++;
                    // Pad each numeric run to 10 digits — covers all reasonable
                    // contract numbers (up to 9_999_999_999 per segment). The
                    // padding is uniform across the dataset, so string compare
                    // produces the same order as numeric compare.
                    string num = value.Substring(start, i - start);
                    if (num.Length < 10) sb.Append('0', 10 - num.Length);
                    sb.Append(num);
                }
                else
                {
                    sb.Append(value[i]);
                    i++;
                }
            }
            return sb.ToString();
        }
    }

    public static class OrderStatuses
    {
        public static readonly string[] All = {
            "Новый",
            "Подтверждён",
            "Отправлен на завод",
            "В производстве",
            "Готов к установке",
            "Установлен",
            "Оплачен",
            "Отменён"
        };

        /// <summary>
        /// Single source of truth for the order-status badge visual: keys of the
        /// theme tokens (<c>Badge*Bg</c>/<c>Badge*Fg</c>) used by the badge
        /// converters and by the status dot in the sidebar.
        ///
        /// <para>До v3.53 здесь лежала вторая палитра — hex-литералы светлой
        /// темы, — и она разъехалась с токенами: тёмная тема давала бейдж
        /// (яркий мятный, см. ThemeService.DarkColors), а точка того же статуса
        /// оставалась на старом светлом зелёном литерале.
        /// Теперь цвета не дублируются: статус → ключ токена, и оба потребителя
        /// берут кисть из ресурсов приложения (значит, следуют теме).
        /// Существование ключей в обеих темах стережёт
        /// <c>OrderStatusesTests</c>.</para>
        ///
        /// <para>Hex-значения в этом комментарии не приводим намеренно: стражи
        /// <c>HexColorsInCode_LiveOnlyInTheColorLayerOrPrintSchematics</c> считают
        /// hex-литералы в C# без вырезания комментариев (в отличие от XAML), и
        /// упоминание цвета здесь выглядело бы как утечка палитры.</para>
        /// </summary>

        public static (string BackgroundKey, string ForegroundKey) GetBadgeKeys(string status) => status switch
        {
            "Подтверждён"        => ("BadgeSuccessBg", "BadgeSuccessFg"),
            "Отправлен на завод" => ("BadgeWarningBg", "BadgeWarningFg"),
            "В производстве"     => ("BadgeWarningBg", "BadgeWarningFg"),
            "Готов к установке"  => ("BadgeSuccessBg", "BadgeSuccessFg"),
            "Установлен"         => ("BadgeSuccessBg", "BadgeSuccessFg"),
            "Оплачен"            => ("BadgeSuccessBg", "BadgeSuccessFg"),
            "Отменён"            => ("BadgeDangerBg", "BadgeDangerFg"),
            _                    => ("BadgeDefaultBg", "BadgeDefaultFg"),
        };

        /// <summary>
        /// Stable rank of a status in the lifecycle workflow — used by
        /// the «Статус» column in the Orders grid as its SortMemberPath
        /// (via OrderData.StatusRank) so a click on the column header
        /// sorts by logical progress (Новый → Подтверждён → … →
        /// Оплачен/Отменён) instead of by Cyrillic alphabetical
        /// collation. Unknown statuses sink to <see cref="int.MaxValue"/>
        /// so they never interleave into the recognised workflow.
        /// </summary>
        public static int GetRank(string status) => status switch
        {
            "Новый"               => 0,
            "Подтверждён"         => 1,
            "Отправлен на завод"  => 2,
            "В производстве"      => 3,
            "Готов к установке"   => 4,
            "Установлен"          => 5,
            "Оплачен"             => 6,
            "Отменён"             => 7,
            _                     => int.MaxValue,
        };
    }
}

