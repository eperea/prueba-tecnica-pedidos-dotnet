namespace Orders.Api.Domain;

public class Order
{
    public Guid Id { get; set; }
    public required string CustomerId { get; set; }
    public OrderStatus Status { get; set; }
    public decimal Total { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? PaymentReference { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}
