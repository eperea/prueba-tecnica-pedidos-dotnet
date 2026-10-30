using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Orders.Api.Payments;

/// <summary>
/// Simula un proveedor de pagos externo. El resultado depende del token de pago:
/// <c>tok_declined</c> rechaza, <c>tok_gateway_error</c> simula un 503 del proveedor,
/// <c>tok_timeout</c> nunca responde y cualquier otro token aprueba.
/// Es idempotente por pedido, como un proveedor real que recibe llave de idempotencia.
/// </summary>
public class FakePaymentGateway(IOptions<PaymentOptions> options, ILogger<FakePaymentGateway> logger) : IPaymentGateway
{
    public const string DeclinedToken = "tok_declined";
    public const string GatewayErrorToken = "tok_gateway_error";
    public const string TimeoutToken = "tok_timeout";

    private readonly ConcurrentDictionary<Guid, PaymentResult> _processed = new();

    public async Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken cancellationToken)
    {
        if (_processed.TryGetValue(request.OrderId, out var previous))
            return previous;

        if (options.Value.SimulatedLatencyMs > 0)
            await Task.Delay(options.Value.SimulatedLatencyMs, cancellationToken);

        PaymentResult result;
        switch (request.PaymentToken)
        {
            case TimeoutToken:
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new UnreachableException();
            case GatewayErrorToken:
                throw new PaymentGatewayException("El proveedor de pagos respondió 503 Service Unavailable.");
            case DeclinedToken:
                result = PaymentResult.Declined("Fondos insuficientes");
                break;
            default:
                result = PaymentResult.Success("PAY-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant());
                break;
        }

        logger.LogInformation("Pago simulado del pedido {OrderId} por {Amount} {Currency}: {Result}",
            request.OrderId, request.Amount, request.Currency, result.Approved ? "aprobado" : "rechazado");
        return _processed.GetOrAdd(request.OrderId, result);
    }
}
