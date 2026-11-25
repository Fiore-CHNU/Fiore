using System.Net;
using System.Net.Http.Json;
using Fiore.Api.Domain.Catalog;
using Fiore.Api.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Fiore.Api.Tests.Features.Catalog.ListProducts;

public sealed class ListProductsEndpointTests : IClassFixture<ListProductsWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ListProductsEndpointTests(ListProductsWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_returns_a_paginated_catalogue_response()
    {
        var response = await _client.GetAsync("/api/v1/catalog/products?page=1&pageSize=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var document = await response.Content.ReadFromJsonAsync<CatalogResponse>();
        document.Should().NotBeNull();
        document!.Items.Should().ContainSingle();
        document.Items[0].Sku.Should().Be("FIO-002");
        document.Items[0].IsAvailable.Should().BeTrue();
        document.Pagination.Should().Be(new PaginationResponse(Page: 1, PageSize: 1, TotalItems: 2, TotalPages: 2));
    }

    [Fact]
    public async Task Get_rejects_invalid_pagination()
    {
        var response = await _client.GetAsync("/api/v1/catalog/products?page=0&pageSize=101");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delivery_slots_are_available_to_the_checkout_client()
    {
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        var response = await _client.GetAsync($"/api/v1/delivery/slots?date={tomorrow:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var document = await response.Content.ReadFromJsonAsync<DeliverySlotsResponse>();
        document!.Date.Should().Be(tomorrow);
        document.Slots.Should().HaveCount(5);
        document.Slots.Should().OnlyContain(slot => slot.RemainingCapacity >= 0 && slot.RemainingCapacity <= 10 && slot.FeeAmount == 100m);
    }

    [Fact]
    public async Task Get_product_details_returns_the_public_product_with_its_images()
    {
        var catalogue = await _client.GetFromJsonAsync<CatalogResponse>("/api/v1/catalog/products?page=1&pageSize=1");

        var response = await _client.GetAsync($"/api/v1/catalog/products/{catalogue!.Items[0].Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var product = await response.Content.ReadFromJsonAsync<CatalogProductDetails>();
        product.Should().NotBeNull();
        product!.Sku.Should().Be("FIO-002");
        product.IsAvailable.Should().BeTrue();
        product.Images.Should().BeEmpty();
    }

    [Fact]
    public async Task Guest_can_create_a_cart_add_a_product_and_checkout()
    {
        var catalogue = await _client.GetFromJsonAsync<CatalogResponse>("/api/v1/catalog/products?page=1&pageSize=1");
        var productId = catalogue!.Items[0].Id;

        var createCartResponse = await _client.PostAsync("/api/v1/carts", content: null);
        createCartResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var cart = await createCartResponse.Content.ReadFromJsonAsync<CartResponse>();
        cart.Should().NotBeNull();

        _client.DefaultRequestHeaders.Add("X-Cart-Token", cart!.SessionToken.ToString());
        try
        {
            var addItemResponse = await _client.PostAsJsonAsync($"/api/v1/carts/{cart.Id}/items", new { productId, quantity = 2 });
            addItemResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var scheduledFrom = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1).AddHours(10), TimeSpan.Zero);
            var checkoutResponse = await _client.PostAsJsonAsync("/api/v1/checkout", new
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

            checkoutResponse.StatusCode.Should().Be(HttpStatusCode.Created);
            var order = await checkoutResponse.Content.ReadFromJsonAsync<CheckoutResponse>();
            order.Should().NotBeNull();
            order!.Status.Should().Be("pendingPayment");
            order.TotalAmount.Should().Be(1100);

            var orderResponse = await _client.GetAsync($"/api/v1/orders/{order.OrderId}");
            orderResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var persistedOrder = await orderResponse.Content.ReadFromJsonAsync<PersistedOrderResponse>();
            persistedOrder.Should().NotBeNull();
            persistedOrder!.Items.Should().ContainSingle();
            persistedOrder.Items[0].Quantity.Should().Be(2);
            persistedOrder.Delivery.City.Should().Be("Chernivtsi");
            persistedOrder.Payment.Status.Should().Be("initiated");

            var paymentResponse = await _client.PostAsync($"/api/v1/orders/{order.OrderId}/payments/sandbox/confirm", content: null);
            paymentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var payment = await paymentResponse.Content.ReadFromJsonAsync<PaymentResponse>();
            payment!.PaymentStatus.Should().Be("succeeded");
            payment.OrderStatus.Should().Be("paid");

            var paidOrderResponse = await _client.GetAsync($"/api/v1/orders/{order.OrderId}");
            var paidOrder = await paidOrderResponse.Content.ReadFromJsonAsync<PersistedOrderResponse>();
            paidOrder!.Status.Should().Be("paid");
            paidOrder.StatusHistory.Should().HaveCount(2);

            var repeatedCheckoutResponse = await _client.PostAsJsonAsync("/api/v1/checkout", new
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

            repeatedCheckoutResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            _client.DefaultRequestHeaders.Remove("X-Cart-Token");
        }
    }

    [Fact]
    public async Task Guest_cannot_add_more_items_than_the_available_inventory()
    {
        var catalogue = await _client.GetFromJsonAsync<CatalogResponse>("/api/v1/catalog/products?page=1&pageSize=1");
        var createCartResponse = await _client.PostAsync("/api/v1/carts", content: null);
        var cart = await createCartResponse.Content.ReadFromJsonAsync<CartResponse>();

        _client.DefaultRequestHeaders.Add("X-Cart-Token", cart!.SessionToken.ToString());
        try
        {
            var response = await _client.PostAsJsonAsync($"/api/v1/carts/{cart.Id}/items", new
            {
                productId = catalogue!.Items[0].Id,
                quantity = 6
            });

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        finally
        {
            _client.DefaultRequestHeaders.Remove("X-Cart-Token");
        }
    }

    [Fact]
    public async Task Catalog_responses_are_cached_and_evicted_on_invalidation()
    {
        using var factory = new ListProductsWebApplicationFactory();
        using var client = factory.CreateClient();

        var initial = await client.GetFromJsonAsync<CatalogResponse>("/api/v1/catalog/products?page=1&pageSize=12");
        initial!.Pagination.TotalItems.Should().Be(2);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FioreDbContext>();
            var productId = Guid.NewGuid();
            dbContext.Products.Add(new Product
            {
                Id = productId,
                Sku = "FIO-CACHE-TEST",
                Name = "Cached test bouquet",
                BasePrice = 700,
                Currency = "UAH",
                Status = ProductStatus.Available,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Inventory = new ProductInventory
                {
                    ProductId = productId,
                    QuantityAvailable = 3,
                    ReorderLevel = 1,
                    UpdatedAt = DateTimeOffset.UtcNow
                }
            });
            await dbContext.SaveChangesAsync();
        }

        var cached = await client.GetFromJsonAsync<CatalogResponse>("/api/v1/catalog/products?page=1&pageSize=12");
        cached!.Pagination.TotalItems.Should().Be(2);

        using (var scope = factory.Services.CreateScope())
        {
            var cacheStore = scope.ServiceProvider.GetRequiredService<IOutputCacheStore>();
            await cacheStore.EvictByTagAsync("catalog", CancellationToken.None);
        }

        var refreshed = await client.GetFromJsonAsync<CatalogResponse>("/api/v1/catalog/products?page=1&pageSize=12");
        refreshed!.Pagination.TotalItems.Should().Be(3);
    }

    private sealed record CatalogResponse(IReadOnlyList<CatalogItem> Items, PaginationResponse Pagination);
    private sealed record CatalogItem(Guid Id, string Sku, bool IsAvailable);
    private sealed record CatalogProductDetails(string Sku, bool IsAvailable, IReadOnlyList<CatalogImage> Images);
    private sealed record CatalogImage(string ImageUrl);
    private sealed record CartResponse(Guid Id, Guid SessionToken);
    private sealed record CheckoutResponse(Guid OrderId, string Status, decimal TotalAmount);
    private sealed record PersistedOrderResponse(string Status, IReadOnlyList<PersistedOrderItem> Items, PersistedDelivery Delivery, PersistedPayment Payment, IReadOnlyList<PersistedStatusHistory> StatusHistory);
    private sealed record PersistedOrderItem(int Quantity);
    private sealed record PersistedDelivery(string City);
    private sealed record PersistedPayment(string Status);
    private sealed record PersistedStatusHistory(string Status);
    private sealed record PaymentResponse(string PaymentStatus, string OrderStatus);
    private sealed record DeliverySlotsResponse(DateOnly Date, IReadOnlyList<DeliverySlotResponse> Slots);
    private sealed record DeliverySlotResponse(int RemainingCapacity, decimal FeeAmount);
    private sealed record PaginationResponse(int Page, int PageSize, int TotalItems, int TotalPages);
}

public sealed class ListProductsWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"catalog-endpoint-tests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<FioreDbContext>>();
            services.AddDbContext<FioreDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            using var scope = services.BuildServiceProvider().CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<FioreDbContext>();
            dbContext.Database.EnsureDeleted();
            dbContext.Database.EnsureCreated();

            dbContext.Products.AddRange(
                CreateProduct("FIO-001", 1, 0),
                CreateProduct("FIO-002", 2, 5),
                CreateProduct("FIO-003", 3, 2, ProductStatus.Hidden));
            dbContext.SaveChanges();
        });
    }

    private static Product CreateProduct(string sku, int createdOrder, int quantityAvailable, ProductStatus status = ProductStatus.Available)
    {
        var productId = Guid.NewGuid();

        return new Product
        {
            Id = productId,
            Sku = sku,
            Name = sku,
            BasePrice = 500,
            Currency = "UAH",
            Status = status,
            CreatedAt = DateTimeOffset.UnixEpoch.AddDays(createdOrder),
            UpdatedAt = DateTimeOffset.UnixEpoch,
            Inventory = new ProductInventory
            {
                ProductId = productId,
                QuantityAvailable = quantityAvailable,
                ReorderLevel = 1,
                UpdatedAt = DateTimeOffset.UnixEpoch
            }
        };
    }
}
