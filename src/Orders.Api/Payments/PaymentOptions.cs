namespace Orders.Api.Payments;

public class PaymentOptions
{
    public const string Section = "Payments";

    /// <summary>Latencia artificial del proveedor simulado.</summary>
    public int SimulatedLatencyMs { get; set; } = 300;

    /// <summary>Tiempo máximo de espera de la respuesta del proveedor.</summary>
    public int TimeoutSeconds { get; set; } = 5;
}
