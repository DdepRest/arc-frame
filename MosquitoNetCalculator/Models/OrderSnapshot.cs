using System.Collections.Generic;

namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// A complete snapshot of the order state used for undo/redo.
    /// Includes both the order item list and the additional КП list.
    /// </summary>
    public class OrderSnapshot
    {
        public List<OrderItem> Items { get; set; } = new();
        public List<AdditionalKpItem> AdditionalKps { get; set; } = new();
    }
}
