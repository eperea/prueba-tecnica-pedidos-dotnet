namespace Orders.Api.Payments;

public interface IPaymentGateway
{
    /// <summary>
    /// Cobra el valor del pedido. Devuelve aprobado o rechazado; lanza
    /// <see cref="PaymentGatewayException"/> si el proveedor no está disponible.
    /// </summary>
    Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken cancellationToken);
}

/// <param name="OrderId">Se envía al proveedor como llave de idempotencia.</param>
public sealed record PaymentRequest(Guid OrderId, decimal Amount, string Currency, string PaymentToken);

public sealed record PaymentResult(bool Approved, string? Reference, string? DeclineReason)
{
    public static PaymentResult Success(string reference) => new(true, reference, null);
    public static PaymentResult Declined(string reason) => new(false, null, reason);
}

public class PaymentGatewayException(string message, Exception? inner = null) : Exception(message, inner);
