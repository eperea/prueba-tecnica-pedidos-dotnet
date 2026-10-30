using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Orders.Api.Contracts;
using Orders.Api.Domain;
using Orders.Api.Services;

namespace Orders.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController(OrderService orders) : ControllerBase
{
    /// <summary>Crea un pedido: reserva inventario y cobra con el proveedor de pagos.</summary>
    /// <param name="request">Cliente, productos y token de pago.</param>
    /// <param name="idempotencyKey">
    /// Opcional. Si se repite la misma llave (por ejemplo, un reintento tras un timeout de red),
    /// se devuelve el pedido original en vez de crear otro.
    /// </param>
    /// <response code="201">Pedido creado y pagado.</response>
    /// <response code="200">Reintento con una llave de idempotencia ya usada: se devuelve el pedido existente.</response>
    /// <response code="400">Solicitud inválida.</response>
    /// <response code="402">El proveedor rechazó el pago. El pedido queda registrado y el inventario liberado.</response>
    /// <response code="409">No hay inventario suficiente.</response>
    /// <response code="422">Algún producto no existe.</response>
    /// <response code="502">El proveedor de pagos falló o no respondió. El inventario se liberó.</response>
    [HttpPost]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Create(
        CreateOrderRequest request,
        [FromHeader(Name = "Idempotency-Key"), StringLength(100)] string? idempotencyKey,
        CancellationToken ct)
    {
        var result = await orders.CreateAsync(request, idempotencyKey, ct);
        var response = OrderResponse.From(result.Order);

        if (result.Replayed)
            return Ok(response);

        return result.Order.Status switch
        {
            OrderStatus.Paid => CreatedAtAction(nameof(GetById), new { id = response.Id }, response),
            OrderStatus.PaymentDeclined => PaymentProblem(StatusCodes.Status402PaymentRequired, "Pago rechazado", response),
            _ => PaymentProblem(StatusCodes.Status502BadGateway, "Proveedor de pagos no disponible", response)
        };
    }

    /// <summary>Consulta un pedido por id.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var order = await orders.GetAsync(id, ct);
        return order is null ? NotFound() : Ok(OrderResponse.From(order));
    }

    /// <summary>Lista pedidos, del más reciente al más antiguo, con filtros opcionales.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<OrderResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<PagedResponse<OrderResponse>> List(
        [FromQuery] string? customerId,
        [FromQuery] OrderStatus? status,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await orders.ListAsync(customerId, status, page, pageSize, ct);
        return new PagedResponse<OrderResponse>(
            result.Items.Select(OrderResponse.From).ToList(), result.Page, result.PageSize, result.TotalCount);
    }

    private ObjectResult PaymentProblem(int statusCode, string title, OrderResponse order)
    {
        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, statusCode, title, detail: order.FailureReason);
        problem.Extensions["orderId"] = order.Id;
        problem.Extensions["order"] = order;
        return new ObjectResult(problem) { StatusCode = statusCode, ContentTypes = { "application/problem+json" } };
    }
}
