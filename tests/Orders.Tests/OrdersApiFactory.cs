using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orders.Api.Data;
using Orders.Api.Domain;

namespace Orders.Tests;

/// <summary>Levanta la API en memoria contra un archivo SQLite temporal propio de cada clase de pruebas.</summary>
public class OrdersApiFactory : WebApplicationFactory<Program>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"orders-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Orders", $"Data Source={_dbPath}");
        builder.UseSetting("Payments:SimulatedLatencyMs", "0");
        builder.UseSetting("Payments:TimeoutSeconds", "1");
    }

    public async Task<int> CreateProductAsync(int stock, decimal price = 10_000m)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = new Product
        {
            Sku = "T-" + Guid.NewGuid().ToString("N")[..20],
            Name = "Producto de prueba",
            Price = price,
            Stock = stock
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    public async Task<int> GetStockAsync(int productId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Products.Where(p => p.Id == productId).Select(p => p.Stock).SingleAsync();
    }

    public async Task<int> CountOrdersAsync(string customerId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Orders.CountAsync(o => o.CustomerId == customerId);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }
}
