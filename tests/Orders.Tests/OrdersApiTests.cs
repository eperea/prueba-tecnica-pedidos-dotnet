using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Orders.Api.Contracts;
using Orders.Api.Domain;
using Orders.Api.Payments;

namespace Orders.Tests;

public class OrdersApiTests(OrdersApiFactory factory) : IClassFixture<OrdersApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static CreateOrderRequest NewOrder(string customerId, string paymentToken = "tok_ok", params (int ProductId, int Quantity)[] lines) => new()
    {
        CustomerId = customerId,
        PaymentToken = paymentToken,
        Items = lines.Select(l => new OrderLineRequest { ProductId = l.ProductId, Quantity = l.Quantity }).ToList()
    };

    private static string NewCustomer() => "c-" + Guid.NewGuid().ToString("N")[..8];

    private Task<HttpResponseMessage> PostAsync(CreateOrderRequest request, string? idempotencyKey = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/orders") { Content = JsonContent.Create(request) };
        if (idempotencyKey is not null)
            message.Headers.Add("Idempotency-Key", idempotencyKey);
        return _client.SendAsync(message);
    }

    private static async Task<OrderResponse> ReadOrderAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<OrderResponse>(OrdersApiFactory.Json))!;

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Create_with_enough_stock_returns_201_charges_and_decrements_stock()
    {
        var productId = await factory.CreateProductAsync(stock: 10, price: 25_000m);

        var response = await PostAsync(NewOrder(NewCustomer(), "tok_ok", (productId, 3)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await ReadOrderAsync(response);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(75_000m, order.Total);
        Assert.NotNull(order.PaymentReference);
        Assert.Equal($"/api/orders/{order.Id}", response.Headers.Location?.AbsolutePath);
        Assert.Equal(7, await factory.GetStockAsync(productId));
    }

    [Fact]
    public async Task Create_with_more_than_available_returns_409_and_keeps_stock()
    {
        var productId = await factory.CreateProductAsync(stock: 2);

        var response = await PostAsync(NewOrder(NewCustomer(), "tok_ok", (productId, 3)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(2, problem.GetProperty("available").GetInt32());
        Assert.Equal(2, await factory.GetStockAsync(productId));
    }

    [Fact]
    public async Task Concurrent_orders_never_oversell()
    {
        const int stock = 5;
        const int buyers = 30;
        var productId = await factory.CreateProductAsync(stock);

        var responses = await Task.WhenAll(Enumerable.Range(0, buyers)
            .Select(_ => PostAsync(NewOrder(NewCustomer(), "tok_ok", (productId, 1)))));

        Assert.Equal(stock, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(buyers - stock, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(0, await factory.GetStockAsync(productId));
    }

    [Fact]
    public async Task Order_is_all_or_nothing_when_one_line_lacks_stock()
    {
        var available = await factory.CreateProductAsync(stock: 10);
        var scarce = await factory.CreateProductAsync(stock: 1);
        var customer = NewCustomer();

        var response = await PostAsync(NewOrder(customer, "tok_ok", (available, 4), (scarce, 2)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(10, await factory.GetStockAsync(available));
        Assert.Equal(1, await factory.GetStockAsync(scarce));
        Assert.Equal(0, await factory.CountOrdersAsync(customer));
    }

    [Fact]
    public async Task Repeated_lines_of_same_product_are_merged_before_checking_stock()
    {
        var productId = await factory.CreateProductAsync(stock: 3);

        var response = await PostAsync(NewOrder(NewCustomer(), "tok_ok", (productId, 2), (productId, 2)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(3, await factory.GetStockAsync(productId));
    }

    [Fact]
    public async Task Declined_payment_returns_402_and_releases_stock()
    {
        var productId = await factory.CreateProductAsync(stock: 5);

        var response = await PostAsync(NewOrder(NewCustomer(), FakePaymentGateway.DeclinedToken, (productId, 2)));

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        var orderId = problem.GetProperty("orderId").GetGuid();
        Assert.Equal(5, await factory.GetStockAsync(productId));

        var stored = await ReadOrderAsync(await _client.GetAsync($"/api/orders/{orderId}"));
        Assert.Equal(OrderStatus.PaymentDeclined, stored.Status);
        Assert.Equal("Fondos insuficientes", stored.FailureReason);
    }

    [Theory]
    [InlineData(FakePaymentGateway.GatewayErrorToken)]
    [InlineData(FakePaymentGateway.TimeoutToken)]
    public async Task Payment_gateway_failure_returns_502_and_releases_stock(string token)
    {
        var productId = await factory.CreateProductAsync(stock: 5);

        var response = await PostAsync(NewOrder(NewCustomer(), token, (productId, 2)));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(nameof(OrderStatus.PaymentFailed), problem.GetProperty("order").GetProperty("status").GetString());
        Assert.Equal(5, await factory.GetStockAsync(productId));
    }

    [Fact]
    public async Task Same_idempotency_key_creates_a_single_order()
    {
        var productId = await factory.CreateProductAsync(stock: 10);
        var customer = NewCustomer();
        var key = Guid.NewGuid().ToString();
        var request = NewOrder(customer, "tok_ok", (productId, 2));

        var first = await PostAsync(request, key);
        var retry = await PostAsync(request, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal((await ReadOrderAsync(first)).Id, (await ReadOrderAsync(retry)).Id);
        Assert.Equal(8, await factory.GetStockAsync(productId));
        Assert.Equal(1, await factory.CountOrdersAsync(customer));
    }

    [Fact]
    public async Task Concurrent_requests_with_same_idempotency_key_create_a_single_order()
    {
        var productId = await factory.CreateProductAsync(stock: 10);
        var customer = NewCustomer();
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => PostAsync(NewOrder(customer, "tok_ok", (productId, 1)), key)));

        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, $"Respuesta inesperada: {r.StatusCode}"));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(9, await factory.GetStockAsync(productId));
        Assert.Equal(1, await factory.CountOrdersAsync(customer));
    }

    [Fact]
    public async Task Unknown_product_returns_422()
    {
        var response = await PostAsync(NewOrder(NewCustomer(), "tok_ok", (999_999, 1)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await ReadProblemAsync(response);
    }

    public static TheoryData<string> InvalidPayloads => new()
    {
        """{"customerId":"c1","paymentToken":"tok_ok","items":[]}""",
        """{"customerId":"c1","paymentToken":"tok_ok","items":[{"productId":1,"quantity":0}]}""",
        """{"customerId":"c1","paymentToken":"tok_ok","items":[{"productId":1,"quantity":-3}]}""",
        """{"customerId":"","paymentToken":"tok_ok","items":[{"productId":1,"quantity":1}]}""",
        """{"customerId":"c1","items":[{"productId":1,"quantity":1}]}""",
        """{"customerId":"c1","paymentToken":"tok_ok","items":"no-es-una-lista"}""",
    };

    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public async Task Invalid_payload_returns_400(string json)
    {
        var response = await _client.PostAsync("/api/orders",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await ReadProblemAsync(response);
    }

    [Fact]
    public async Task Get_unknown_order_returns_404()
    {
        var response = await _client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_order_returns_items_with_price_snapshot()
    {
        var productId = await factory.CreateProductAsync(stock: 10, price: 12_500m);
        var created = await ReadOrderAsync(await PostAsync(NewOrder(NewCustomer(), "tok_ok", (productId, 2))));

        var response = await _client.GetAsync($"/api/orders/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = await ReadOrderAsync(response);
        var item = Assert.Single(order.Items);
        Assert.Equal(productId, item.ProductId);
        Assert.Equal(12_500m, item.UnitPrice);
        Assert.Equal(25_000m, item.Subtotal);
        Assert.Equal(25_000m, order.Total);
    }

    [Fact]
    public async Task List_filters_by_customer_and_status()
    {
        var productId = await factory.CreateProductAsync(stock: 10);
        var customer = NewCustomer();
        await PostAsync(NewOrder(customer, "tok_ok", (productId, 1)));
        await PostAsync(NewOrder(customer, FakePaymentGateway.DeclinedToken, (productId, 1)));
        await PostAsync(NewOrder(NewCustomer(), "tok_ok", (productId, 1)));

        var all = await _client.GetFromJsonAsync<PagedResponse<OrderResponse>>($"/api/orders?customerId={customer}", OrdersApiFactory.Json);
        var paid = await _client.GetFromJsonAsync<PagedResponse<OrderResponse>>($"/api/orders?customerId={customer}&status=Paid", OrdersApiFactory.Json);

        Assert.Equal(2, all!.TotalCount);
        Assert.All(all.Items, o => Assert.Equal(customer, o.CustomerId));
        Assert.Equal(OrderStatus.Paid, Assert.Single(paid!.Items).Status);
    }
}
