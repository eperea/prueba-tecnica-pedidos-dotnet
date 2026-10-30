namespace Orders.Api.Domain;

public enum OrderStatus
{
    /// <summary>Inventario reservado, esperando respuesta del proveedor de pagos.</summary>
    PendingPayment,
    /// <summary>Pago aprobado. Estado final.</summary>
    Paid,
    /// <summary>El proveedor rechazó el pago; el inventario se liberó. Estado final.</summary>
    PaymentDeclined,
    /// <summary>El proveedor falló o no respondió a tiempo; el inventario se liberó. Estado final.</summary>
    PaymentFailed
}
