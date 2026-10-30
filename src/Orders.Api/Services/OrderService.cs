using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orders.Api.Contracts;
using Orders.Api.Data;
using Orders.Api.Domain;
using Orders.Api.Payments;

namespace Orders.Api.Services;

/// <param name="Replayed">true si el pedido ya existía para la misma llave de idempotencia.</param>
public sealed record CreateOrderResult(Order Order, bool Replayed);

public class OrderService(
    AppDbContext db,
    IPaymentGateway paymentGateway,
    IOptions<PaymentOptions> paymentOptions,
    ILogger<OrderService> logger)
{
    private const string Currency = "COP";

    /// <summary>
    /// Flujo de creación:
    /// 1. Reserva el inventario y guarda el pedido en una sola transacción.
    /// 2. Llama al proveedor de pagos fuera de la transacción (no se bloquea la BD esperando una red externa).
    /// 3. Si el pago no se aprueba, devuelve el inventario (compensación).
    /// </summary>
    public async Task<CreateOrderResult> CreateAsync(CreateOrderRequest request, string? idempotencyKey, CancellationToken ct)
    {
        if (idempotencyKey is not null && await FindByIdempotencyKeyAsync(idempotencyKey, ct) is { } existing)
            return new CreateOrderResult(existing, Replayed: true);

        // Agrupa líneas repetidas del mismo producto y las ordena por id para que dos pedidos
        // concurrentes bloqueen los productos en el mismo orden (evita interbloqueos en motores con bloqueo por fila).
        var lines = request.Items
            .GroupBy(i => i.ProductId)
            .Select(g => (ProductId: g.Key, Quantity: g.Sum(i => i.Quantity)))
            .OrderBy(l => l.ProductId)
            .ToList();

        var productIds = lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var missing = productIds.Where(id => !products.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            throw new ProductNotFoundException(missing);

        var now = DateTime.UtcNow;
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = request.CustomerId,
            Status = OrderStatus.PendingPayment,
            IdempotencyKey = idempotencyKey,
            CreatedAt = now,
            UpdatedAt = now,
            Items = lines.Select(l => new OrderItem
            {
                ProductId = l.ProductId,
                ProductName = products[l.ProductId].Name,
                UnitPrice = products[l.ProductId].Price,
                Quantity = l.Quantity
            }).ToList()
        };
        order.Total = order.Items.Sum(i => i.Subtotal);

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            foreach (var (productId, quantity) in lines)
            {
                // Descuento atómico: el WHERE garantiza que nunca quede stock negativo aunque
                // lleguen muchas solicitudes a la vez. Si no actualiza filas, no alcanza el inventario.
                var updated = await db.Products
                    .Where(p => p.Id == productId && p.Stock >= quantity)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock - quantity), ct);

                if (updated == 0)
                {
                    await tx.RollbackAsync(ct);
                    var available = await db.Products.Where(p => p.Id == productId).Select(p => p.Stock).SingleAsync(ct);
                    throw new InsufficientStockException(productId, quantity, available);
                }
            }

            db.Orders.Add(order);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException) when (idempotencyKey is not null)
            {
                // Otra solicitud con la misma llave ganó la carrera: se deshace la reserva y se devuelve su pedido.
                await tx.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                if (await FindByIdempotencyKeyAsync(idempotencyKey, ct) is { } winner)
                    return new CreateOrderResult(winner, Replayed: true);
                throw;
            }

            await tx.CommitAsync(ct);
        }

        // Desde aquí el inventario ya está reservado: se ignora la cancelación del cliente para
        // no dejar el pedido a medias si cierra la conexión mientras se procesa el pago.
        await ProcessPaymentAsync(order, request.PaymentToken);
        return new CreateOrderResult(order, Replayed: false);
    }

    public Task<Order?> GetAsync(Guid id, CancellationToken ct) =>
        db.Orders.AsNoTracking().Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == id, ct);

    public async Task<PagedResponse<Order>> ListAsync(string? customerId, OrderStatus? status, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Orders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(customerId))
            query = query.Where(o => o.CustomerId == customerId);
        if (status is not null)
            query = query.Where(o => o.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResponse<Order>(items, page, pageSize, total);
    }

    private async Task ProcessPaymentAsync(Order order, string paymentToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(paymentOptions.Value.TimeoutSeconds));
        PaymentResult result;
        try
        {
            result = await paymentGateway.ChargeAsync(
                new PaymentRequest(order.Id, order.Total, Currency, paymentToken), timeout.Token);
        }
        catch (Exception ex) when (ex is PaymentGatewayException || (ex is OperationCanceledException && timeout.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Falló el proveedor de pagos para el pedido {OrderId}", order.Id);
            await ReleaseAsync(order, OrderStatus.PaymentFailed, "El proveedor de pagos no está disponible. Intente de nuevo.");
            return;
        }

        if (result.Approved)
        {
            order.Status = OrderStatus.Paid;
            order.PaymentReference = result.Reference;
            order.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        else
        {
            await ReleaseAsync(order, OrderStatus.PaymentDeclined, result.DeclineReason ?? "Pago rechazado");
        }
    }

    /// <summary>Devuelve al inventario lo reservado y deja el pedido en un estado final de fallo.</summary>
    private async Task ReleaseAsync(Order order, OrderStatus finalStatus, string reason)
    {
        await using var tx = await db.Database.BeginTransactionAsync(CancellationToken.None);
        foreach (var item in order.Items)
        {
            await db.Products
                .Where(p => p.Id == item.ProductId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock + item.Quantity), CancellationToken.None);
        }

        order.Status = finalStatus;
        order.FailureReason = reason;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        await tx.CommitAsync(CancellationToken.None);
    }

    private Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken ct) =>
        db.Orders.AsNoTracking().Include(o => o.Items).SingleOrDefaultAsync(o => o.IdempotencyKey == key, ct);
}
