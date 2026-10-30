using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orders.Api.Contracts;
using Orders.Api.Data;

namespace Orders.Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController(AppDbContext db) : ControllerBase
{
    /// <summary>Lista el catálogo con el inventario disponible.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<ProductResponse>> List(CancellationToken ct) =>
        await db.Products.AsNoTracking().OrderBy(p => p.Id).Select(p => ProductResponse.From(p)).ToListAsync(ct);

    /// <summary>Consulta un producto por id.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        return product is null ? NotFound() : Ok(ProductResponse.From(product));
    }
}
