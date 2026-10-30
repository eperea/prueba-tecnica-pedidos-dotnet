namespace Orders.Api.Domain;

public class OrderItem
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }
    public int ProductId { get; set; }

    // Nombre y precio se copian al crear el pedido: si el producto cambia después,
    // el pedido conserva lo que se cobró.
    public required string ProductName { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }

    public decimal Subtotal => UnitPrice * Quantity;
}
