using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fiore.Api.Domain.Catalog;
using Fiore.Api.Features.Orders;
using Fiore.Api.Features.Payments;
using Fiore.Api.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Fiore.Api.Tests.Features.Payments;

public sealed class PaymentGatewayTests : IClassFixture<PaymentGatewayTests.PaymentTestFactory>
{
    private static readonly TimeZoneInfo Kyiv = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");
    private readonly PaymentTestFactory _factory;
    private readonly HttpClient _client;

    public PaymentGatewayTests(PaymentTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _factory.Gateway.Mode = StubPaymentGateway.FailureMode.None;
        _factory.Time.Set(new DateTimeOffset(new DateTime(2026, 12, 10, 9, 0, 0), TimeSpan.FromHours(2)).ToUniversalTime());
    }

    [Fact]
    public async Task Provider_unavailable_simulation_returns_502_and_allows_retry()
    {
        using var sandboxFactory = new DefaultSandboxFactory();
        var client = sandboxFactory.CreateClient();
        var (orderId, token) = await CreateOrderAsync(client);

        var failedResponse = await PostConfirmAsync(client, $"/api/v1/orders/{orderId}/payments/confirm?outcome=providerUnavailable", token);
        failedResponse.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        using var failedBody = JsonDocument.Parse(await failedResponse.Content.ReadAsStringAsync());
        failedBody.RootElement.GetProperty("error").GetString().Should().Be("PaymentProviderUnavailable");
        failedBody.RootElement.GetProperty("retryable").GetBoolean().Should().BeTrue();

        var retryResponse = await PostConfirmAsync(client, $"/api/v1/orders/{orderId}/payments/confirm", token);
        retryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var retryBody = JsonDocument.Parse(await retryResponse.Content.ReadAsStringAsync());
        retryBody.RootElement.GetProperty("orderStatus").GetString().Should().Be("paid");
    }

    [Fact]
    public async Task Provider_declined_simulation_returns_409_and_allows_retry()
    {
        using var sandboxFactory = new DefaultSandboxFactory();
        var client = sandboxFactory.CreateClient();
        var (orderId, token) = await CreateOrderAsync(client);

        using var declineRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/{orderId}/payments/sandbox/confirm");
        declineRequest.Headers.Add("X-Cart-Token", token.ToString());
        declineRequest.Headers.Add("X-Sandbox-Outcome", "declined");
        var declinedResponse = await client.SendAsync(declineRequest);

        declinedResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var declinedBody = JsonDocument.Parse(await declinedResponse.Content.ReadAsStringAsync());
        declinedBody.RootElement.GetProperty("error").GetString().Should().Be("PaymentDeclined");
        declinedBody.RootElement.GetProperty("retryable").GetBoolean().Should().BeTrue();

        var retryResponse = await PostConfirmAsync(client, $"/api/v1/orders/{orderId}/payments/confirm", token);
        retryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Custom_provider_exception_is_handled_gracefully_with_502_and_retry_succeeds_after_recovery()
    {
        var (orderId, token) = await CreateOrderAsync(_client);
        _factory.Gateway.Mode = StubPaymentGateway.FailureMode.ThrowNetworkError;

        var failedResponse = await PostConfirmAsync(_client, $"/api/v1/orders/{orderId}/payments/confirm", token);
        failedResponse.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        using var failedBody = JsonDocument.Parse(await failedResponse.Content.ReadAsStringAsync());
        failedBody.RootElement.GetProperty("error").GetString().Should().Be("PaymentProviderUnavailable");
        failedBody.RootElement.GetProperty("retryable").GetBoolean().Should().BeTrue();

        _factory.Gateway.Mode = StubPaymentGateway.FailureMode.None;
        var retryResponse = await PostConfirmAsync(_client, $"/api/v1/orders/{orderId}/payments/confirm", token);
        retryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var retryBody = JsonDocument.Parse(await retryResponse.Content.ReadAsStringAsync());
        retryBody.RootElement.GetProperty("provider").GetString().Should().Be("stub-liqpay");
        retryBody.RootElement.GetProperty("orderStatus").GetString().Should().Be("paid");
    }

    [Fact]
    public async Task Expired_order_after_failed_attempt_returns_409_window_expired_and_restores_stock()
    {
        var (orderId, token) = await CreateOrderAsync(_client);
        _factory.Gateway.Mode = StubPaymentGateway.FailureMode.ThrowTimeout;

        var failedResponse = await PostConfirmAsync(_client, $"/api/v1/orders/{orderId}/payments/confirm", token);
        failedResponse.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await StockOfAsync(_factory.Services)).Should().Be(9);

        _factory.Time.Advance(TimeSpan.FromMinutes(15));
        _factory.Gateway.Mode = StubPaymentGateway.FailureMode.None;
        var lateRetry = await PostConfirmAsync(_client, $"/api/v1/orders/{orderId}/payments/confirm", token);

        lateRetry.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var body = JsonDocument.Parse(await lateRetry.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("error").GetString().Should().Be("PaymentWindowExpired");
        body.RootElement.GetProperty("retryable").GetBoolean().Should().BeFalse();
        (await StockOfAsync(_factory.Services)).Should().Be(10);
    }

    private static async Task<int> StockOfAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FioreDbContext>();
        return (await dbContext.ProductInventory.SingleAsync(item => item.ProductId == TestProductId)).QuantityAvailable;
    }

    private static async Task<HttpResponseMessage> PostConfirmAsync(HttpClient client, string url, Guid token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-Cart-Token", token.ToString());
        return await client.SendAsync(request);
    }

    private static async Task<(Guid OrderId, Guid CartToken)> CreateOrderAsync(HttpClient client)
    {
        var cartResponse = await client.PostAsync("/api/v1/carts", null);
        cartResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var cart = await cartResponse.Content.ReadFromJsonAsync<CartDocument>();

        using var addItem = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/carts/{cart!.Id}/items");
        addItem.Headers.Add("X-Cart-Token", cart.SessionToken.ToString());
        addItem.Content = JsonContent.Create(new { productId = TestProductId, quantity = 1 });
        (await client.SendAsync(addItem)).StatusCode.Should().Be(HttpStatusCode.OK);

        var local = new DateTime(2026, 12, 10, 14, 0, 0);
        var scheduledFrom = new DateTimeOffset(local, Kyiv.GetUtcOffset(local)).ToUniversalTime();
        using var checkout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout");
        checkout.Headers.Add("X-Cart-Token", cart.SessionToken.ToString());
        checkout.Content = JsonContent.Create(new
        {
            cartId = cart.Id,
            contactName = "Test Customer",
            contactEmail = "customer@example.test",
            contactPhone = "+380501112233",
            recipientName = "Test Recipient",
            recipientPhone = "+380501112233",
            city = "Chernivtsi",
            street = "Main Street",
            building = "1",
            scheduledFrom,
            scheduledTo = scheduledFrom.AddHours(2)
        });
        var orderResponse = await client.SendAsync(checkout);
        orderResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await orderResponse.Content.ReadFromJsonAsync<CheckoutDocument>();
        return (order!.OrderId, cart.SessionToken);
    }

    private static readonly Guid TestProductId = Guid.Parse("00000000-0000-0000-0000-00000000b001");

    private sealed record CartDocument(Guid Id, Guid SessionToken);
    private sealed record CheckoutDocument(Guid OrderId);

    public sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public void Set(DateTimeOffset now) => _now = now.ToUniversalTime();
        public void Advance(TimeSpan by) => _now += by;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    public sealed class StubPaymentGateway : IPaymentGateway
    {
        public enum FailureMode { None, ThrowNetworkError, ThrowTimeout }

        public FailureMode Mode { get; set; } = FailureMode.None;
        public string ProviderName => "stub-liqpay";

        public Task<PaymentGatewayResult> ChargeAsync(PaymentGatewayRequest request, CancellationToken cancellationToken = default) =>
            Mode switch
            {
                FailureMode.ThrowNetworkError => throw new HttpRequestException("Upstream gateway unreachable"),
                FailureMode.ThrowTimeout => throw new TimeoutException("Gateway timed out"),
                _ => Task.FromResult(PaymentGatewayResult.Succeeded($"liqpay-{request.OrderId:N}"))
            };
    }

    public sealed class PaymentTestFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"payment-gateway-tests-{Guid.NewGuid():N}";
        public TestTimeProvider Time { get; } = new();
        public StubPaymentGateway Gateway { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                ConfigureInMemoryDatabase(services, _databaseName);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Time);
                services.RemoveAll<IPaymentGateway>();
                services.AddSingleton<IPaymentGateway>(Gateway);
            });
        }
    }

    private sealed class DefaultSandboxFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"payment-sandbox-tests-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                ConfigureInMemoryDatabase(services, _databaseName);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new StaticTimeProvider());
            });
        }
    }

    private sealed class StaticTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now = new DateTimeOffset(new DateTime(2026, 12, 10, 9, 0, 0), TimeSpan.FromHours(2)).ToUniversalTime();
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static void ConfigureInMemoryDatabase(IServiceCollection services, string databaseName)
    {
        services.RemoveAll<DbContextOptions<FioreDbContext>>();
        services.AddDbContext<FioreDbContext>(options => options.UseInMemoryDatabase(databaseName));
        var expirationService = services.Single(descriptor => descriptor.ImplementationType == typeof(UnpaidOrderExpirationService));
        services.Remove(expirationService);

        using var scope = services.BuildServiceProvider().CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FioreDbContext>();
        dbContext.Database.EnsureDeleted();
        dbContext.Database.EnsureCreated();
        dbContext.Products.Add(new Product
        {
            Id = TestProductId,
            Sku = "PAY-1",
            Name = "Payment Test Bouquet",
            BasePrice = 900m,
            Currency = "UAH",
            Status = ProductStatus.Available,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
            Inventory = new ProductInventory { ProductId = TestProductId, QuantityAvailable = 10, ReorderLevel = 1, UpdatedAt = DateTimeOffset.UnixEpoch }
        });
        dbContext.SaveChanges();
    }
}
