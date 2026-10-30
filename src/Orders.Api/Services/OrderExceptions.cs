namespace Orders.Api.Services;

public class InsufficientStockException(int productId, int requested, int available)
    : Exception($"Inventario insuficiente para el producto {productId}: se pidieron {requested} y hay {available} disponibles.")
{
    public int ProductId { get; } = productId;
    public int Requested { get; } = requested;
    public int Available { get; } = available;
}

public class ProductNotFoundException(IReadOnlyList<int> productIds)
    : Exception($"No existen los productos: {string.Join(", ", productIds)}.")
{
    public IReadOnlyList<int> ProductIds { get; } = productIds;
}
