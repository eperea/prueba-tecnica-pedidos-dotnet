using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Orders.Api.Data;
using Orders.Api.Infrastructure;
using Orders.Api.Payments;
using Orders.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlite(sp.GetRequiredService<IConfiguration>().GetConnectionString("Orders")));

builder.Services.Configure<PaymentOptions>(builder.Configuration.GetSection(PaymentOptions.Section));
// Singleton porque simula un proveedor externo que recuerda los cobros ya hechos (idempotencia).
builder.Services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
builder.Services.AddScoped<OrderService>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "API de pedidos", Version = "v1" });
    c.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml"));
});

var app = builder.Build();

// Para la prueba basta con crear el esquema al arrancar; en producción se usarían migraciones de EF Core.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
}

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapControllers();

app.Run();

public partial class Program;
