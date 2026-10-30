using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Orders.Api.Services;

namespace Orders.Api.Infrastructure;

/// <summary>Traduce las excepciones de negocio a respuestas ProblemDetails (RFC 9457).</summary>
public class DomainExceptionHandler(IProblemDetailsService problemDetails, ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            InsufficientStockException e => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Inventario insuficiente",
                Detail = e.Message,
                Extensions = { ["productId"] = e.ProductId, ["requested"] = e.Requested, ["available"] = e.Available }
            },
            ProductNotFoundException e => new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Producto inexistente",
                Detail = e.Message,
                Extensions = { ["productIds"] = e.ProductIds }
            },
            _ => null
        };

        if (problem is null)
        {
            logger.LogError(exception, "Error no controlado en {Path}", httpContext.Request.Path);
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Error interno",
                Detail = "Ocurrió un error inesperado. Intente de nuevo más tarde."
            };
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
