using Microsoft.EntityFrameworkCore;
using Orders.Api.Domain;

namespace Orders.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(e =>
        {
            e.Property(p => p.Sku).HasMaxLength(32);
            e.Property(p => p.Name).HasMaxLength(200);
            e.Property(p => p.Price).HasPrecision(18, 2);
            e.HasIndex(p => p.Sku).IsUnique();
            // Última barrera contra la sobreventa, además del UPDATE condicional del servicio.
            e.ToTable(t => t.HasCheckConstraint("CK_Products_Stock_NonNegative", "\"Stock\" >= 0"));
            e.HasData(SeedData.Products);
        });

        modelBuilder.Entity<Order>(e =>
        {
            e.Property(o => o.CustomerId).HasMaxLength(64);
            e.Property(o => o.IdempotencyKey).HasMaxLength(100);
            e.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(o => o.Total).HasPrecision(18, 2);
            // SQLite no guarda el tipo de fecha: se marca como UTC al leer.
            e.Property(o => o.CreatedAt).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            e.Property(o => o.UpdatedAt).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            e.HasIndex(o => o.IdempotencyKey).IsUnique();
            e.HasIndex(o => new { o.CustomerId, o.CreatedAt });
            e.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(e =>
        {
            e.Property(i => i.ProductName).HasMaxLength(200);
            e.Property(i => i.UnitPrice).HasPrecision(18, 2);
            e.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
