
using System.ComponentModel.DataAnnotations;

namespace OrderService;

public class Order
{
    [Key]
    public Guid OrderId { get; set; }
    public string? CustomerName { get; set; }
    public List<OrderItem> Items { get; set; } = new();
    public decimal TotalAmount { get; set; }
    public DateTime OrderDate { get; set; }
}

public class OrderItem
{
    [Key]
    public Guid OrderItemId { get; set; }
    public string? ProductId { get; set; }
    public string? ProductName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
