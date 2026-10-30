using System.ComponentModel.DataAnnotations;
using Orders.Api.Domain;

namespace Orders.Api.Contracts;

public sealed record CreateOrderRequest
{
    /// <example>cliente-123</example>
    [Required, StringLength(64, MinimumLength = 1)]
    public string CustomerId { get; init; } = "";

    [Required, MinLength(1), MaxLength(50)]
    public List<OrderLineRequest> Items { get; init; } = [];

    /// <summary>
    /// Token de la tarjeta emitido por el proveedor de pagos. En la simulación:
    /// <c>tok_declined</c> rechaza, <c>tok_gateway_error</c> falla el proveedor,
    /// <c>tok_timeout</c> no responde; cualquier otro aprueba.
    /// </summary>
    /// <example>tok_visa_ok</example>
    [Required, StringLength(100)]
    public string PaymentToken { get; init; } = "";
}

public sealed record OrderLineRequest
{
    /// <example>1</example>
    [Range(1, int.MaxValue)]
    public int ProductId { get; init; }

    /// <example>2</example>
    [Range(1, 1000)]
    public int Quantity { get; init; }
}

public sealed record OrderItemResponse(int ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal Subtotal);

public sealed record OrderResponse(
    Guid Id,
    string CustomerId,
    OrderStatus Status,
    decimal Total,
    string? PaymentReference,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<OrderItemResponse> Items)
{
    public static OrderResponse From(Order order) => new(
        order.Id,
        order.CustomerId,
        order.Status,
        order.Total,
        order.PaymentReference,
        order.FailureReason,
        order.CreatedAt,
        order.UpdatedAt,
        order.Items.Select(i => new OrderItemResponse(i.ProductId, i.ProductName, i.UnitPrice, i.Quantity, i.Subtotal)).ToList());
}

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record ProductResponse(int Id, string Sku, string Name, decimal Price, int Stock)
{
    public static ProductResponse From(Product p) => new(p.Id, p.Sku, p.Name, p.Price, p.Stock);
}
