using Orders.Api.Domain;

namespace Orders.Api.Data;

public static class SeedData
{
    public static readonly Product[] Products =
    [
        new() { Id = 1, Sku = "TEC-001", Name = "Teclado mecánico", Price = 250_000m, Stock = 10 },
        new() { Id = 2, Sku = "MOU-001", Name = "Mouse inalámbrico", Price = 85_000m, Stock = 25 },
        new() { Id = 3, Sku = "MON-001", Name = "Monitor 27 pulgadas", Price = 1_200_000m, Stock = 3 },
        new() { Id = 4, Sku = "CAB-001", Name = "Cable HDMI", Price = 30_000m, Stock = 1 },
    ];
}
