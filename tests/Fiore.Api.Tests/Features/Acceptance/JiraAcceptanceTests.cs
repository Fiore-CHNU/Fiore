using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Fiore.Api.Domain.Catalog;
using Fiore.Api.Domain.Commerce;
using Fiore.Api.Domain.Identity;
using Fiore.Api.Features.Orders;
using Fiore.Api.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Fiore.Api.Tests.Features.Acceptance;

/// <summary>Executable versions of the Jira (EAN) acceptance criteria that the backend owns.</summary>
public sealed class JiraAcceptanceTests : IClassFixture<AcceptanceWebApplicationFactory>
{
    private static readonly TimeZoneInfo Kyiv = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");
    private readonly AcceptanceWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public JiraAcceptanceTests(AcceptanceWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _factory.Time.Set(KyivTime(new DateOnly(2026, 12, 10), 9, 0));
    }

    [Fact]
    public async Task US_4_3_slots_starting_within_90_minutes_are_unavailable()
    {
        var day = new DateOnly(2026, 12, 10);
        _factory.Time.Set(KyivTime(day, 13, 0));

        var document = await _client.GetFromJsonAsync<SlotsDocument>($"/api/v1/delivery/slots?date={day:yyyy-MM-dd}");

        document!.Slots.Select(slot => (CommerceHour(slot.ScheduledFrom), slot.IsAvailable)).Should().Equal(
            (10, false), (12, false), (14, false), (16, true), (18, true));
    }

    [Fact]
    public async Task US_4_3_when_nothing_is_left_today_the_next_day_is_offered()
    {
        var day = new DateOnly(2026, 12, 10);
        _factory.Time.Set(KyivTime(day, 19, 0));

        var document = await _client.GetFromJsonAsync<SlotsDocument>("/api/v1/delivery/slots");

        document!.Date.Should().Be(day.AddDays(1));
        document.Slots.Should().OnlyContain(slot => slot.IsAvailable);
    }

    [Fact]
    public async Task US_4_3_checkout_rejects_a_slot_that_became_too_close()
    {
        var day = new DateOnly(2026, 12, 10);
        _factory.Time.Set(KyivTime(day, 12, 35));
        var cart = await CreateCartWith(AcceptanceWebApplicationFactory.RedBouquetId, 1);

        var response = await Checkout(cart, KyivTime(day, 14, 0));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorOf(response)).Should().Be("DeliverySlotUnavailable");
    }

    [Theory]
    [InlineData("0501112233", "+380501112233", "Chernivtsi", "phone")]
    [InlineData("+380501112233", "+38050111", "Chernivtsi", "recipient phone")]
    [InlineData("+380501112233", "+380501112233", "Kyiv", "city")]
    public async Task US_4_1_and_4_2_invalid_contact_data_returns_400(string contactPhone, string recipientPhone, string city, string because)
    {
        var cart = await CreateCartWith(AcceptanceWebApplicationFactory.RedBouquetId, 1);

        var response = await Checkout(cart, DefaultSlot(), contactPhone: contactPhone, recipientPhone: recipientPhone, city: city);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, because);
    }

    [Fact]
    public async Task US_4_4_card_message_is_limited_to_250_characters()
    {
        var cart = await CreateCartWith(AcceptanceWebApplicationFactory.RedBouquetId, 1);

        var tooLong = await Checkout(cart, DefaultSlot(), cardMessage: new string('a', 251));
        var atLimit = await Checkout(cart, DefaultSlot(), cardMessage: new string('a', 250));

        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        atLimit.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task US_3_2_delivery_fee_and_minimum_order_are_applied()
    {
        var smallCart = await CreateCartWith(AcceptanceWebApplicationFactory.SmallBouquetId, 1);
        var smallCartDocument = await GetCart(smallCart);
        smallCartDocument.MeetsMinimumOrder.Should().BeFalse();
        smallCartDocument.MinimumOrderAmount.Should().Be(500);

        var tooSmall = await Checkout(smallCart, DefaultSlot());
        tooSmall.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorOf(tooSmall)).Should().Be("MinimumOrderNotMet");

        var regularCart = await CreateCartWith(AcceptanceWebApplicationFactory.RedBouquetId, 1);
        var regular = await GetCart(regularCart);
        regular.DeliveryFeeAmount.Should().Be(100);
        regular.TotalAmount.Should().Be(1100);

        var largeCart = await CreateCartWith(AcceptanceWebApplicationFactory.BlueBouquetId, 1);
        (await GetCart(largeCart)).DeliveryFeeAmount.Should().Be(0);
    }

    [Fact]
    public async Task US_4_5_price_change_requires_customer_confirmation_and_price_is_fixed_in_order()
    {
        var productId = await AddProduct(price: 800);
        var cart = await CreateCartWith(productId, 1);
        await ChangePrice(productId, 900);

        var conflict = await Checkout(cart, DefaultSlot());
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using (var body = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync()))
        {
            body.RootElement.GetProperty("error").GetString().Should().Be("PriceChanged");
            var item = body.RootElement.GetProperty("items")[0];
            item.GetProperty("acceptedUnitPrice").GetDecimal().Should().Be(800);
            item.GetProperty("currentUnitPrice").GetDecimal().Should().Be(900);
        }

        (await GetCart(cart)).HasPriceChanges.Should().BeTrue();
        var accept = await Send(HttpMethod.Post, $"/api/v1/carts/{cart.Id}/accept-prices", cart);
        accept.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await Checkout(cart, DefaultSlot());
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await created.Content.ReadFromJsonAsync<CheckoutDocument>();
        await ChangePrice(productId, 1200);
        var persisted = await Send(HttpMethod.Get, $"/api/v1/orders/{order!.OrderId}", cart);
        using var orderBody = JsonDocument.Parse(await persisted.Content.ReadAsStringAsync());
        orderBody.RootElement.GetProperty("items")[0].GetProperty("unitPrice").GetDecimal().Should().Be(900);
    }

    [Fact]
    public async Task US_8_3_staff_switch_marks_a_bouquet_unavailable_and_checkout_names_it()
    {
        var productId = await AddProduct(price: 700);
        var cart = await CreateCartWith(productId, 1);
        await UseStaffToken();
        try
        {
            var toggle = await _client.PutAsJsonAsync($"/api/v1/admin/products/{productId}/availability", new { isAvailable = false });
            toggle.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            _client.DefaultRequestHeaders.Authorization = null;
        }

        var details = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/catalog/products/{productId}");
        details.GetProperty("isAvailable").GetBoolean().Should().BeFalse();

        var response = await Checkout(cart, DefaultSlot());
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("error").GetString().Should().Be("ProductNotAvailable");
        body.RootElement.GetProperty("items")[0].GetProperty("productId").GetGuid().Should().Be(productId);
    }

    [Fact]
    public async Task US_5_2_late_payment_is_refused_and_stock_returns()
    {
        var productId = await AddProduct(price: 700, quantity: 3);
        var cart = await CreateCartWith(productId, 2);
        var created = await Checkout(cart, DefaultSlot());
        var order = await created.Content.ReadFromJsonAsync<CheckoutDocument>();
        (await StockOf(productId)).Should().Be(1);

        _factory.Time.Advance(TimeSpan.FromMinutes(15));
        var payment = await Send(HttpMethod.Post, $"/api/v1/orders/{order!.OrderId}/payments/sandbox/confirm", cart);

        payment.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorOf(payment)).Should().Be("PaymentWindowExpired");
        (await StockOf(productId)).Should().Be(3);
        var persisted = await Send(HttpMethod.Get, $"/api/v1/orders/{order.OrderId}", cart);
        (await persisted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("cancelled");
    }

    [Fact]
    public async Task US_5_2_payment_in_time_is_accepted()
    {
        var cart = await CreateCartWith(AcceptanceWebApplicationFactory.RedBouquetId, 1);
        var order = await (await Checkout(cart, DefaultSlot())).Content.ReadFromJsonAsync<CheckoutDocument>();

        _factory.Time.Advance(TimeSpan.FromMinutes(14));
        var payment = await Send(HttpMethod.Post, $"/api/v1/orders/{order!.OrderId}/payments/sandbox/confirm", cart);

        payment.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task US_6_2_status_is_available_by_random_code_without_personal_data()
    {
        var cart = await CreateCartWith(AcceptanceWebApplicationFactory.RedBouquetId, 1);
        var order = await (await Checkout(cart, DefaultSlot())).Content.ReadFromJsonAsync<CheckoutDocument>();
        order!.TrackingCode.Should().HaveLength(24);

        var tracked = await _client.GetAsync($"/api/v1/orders/track/{order.TrackingCode}");
        tracked.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await tracked.Content.ReadAsStringAsync();
        body.Should().Contain(order.OrderNumber).And.Contain("pendingPayment");
        body.Should().NotContain("+380501112233").And.NotContain("customer@example.test");

        (await _client.GetAsync("/api/v1/orders/track/not-a-real-code")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task US_7_1_five_wrong_passwords_lock_sign_in_for_five_minutes()
    {
        var email = $"locked-{Guid.NewGuid():N}@example.test";
        await AddStaffAsync(email, "CorrectHorseBattery1");

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            (await Login(email, "wrong-password")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        (await Login(email, "wrong-password")).StatusCode.Should().Be((HttpStatusCode)423);
        (await Login(email, "CorrectHorseBattery1")).StatusCode.Should().Be((HttpStatusCode)423);

        _factory.Time.Advance(TimeSpan.FromMinutes(5));
        (await Login(email, "CorrectHorseBattery1")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task US_10_1_out_for_delivery_requires_a_courier_and_history_records_the_staff_member()
    {
        var cart = await CreateCartWith(AcceptanceWebApplicationFactory.RedBouquetId, 1);
        var order = await (await Checkout(cart, DefaultSlot())).Content.ReadFromJsonAsync<CheckoutDocument>();
        (await Send(HttpMethod.Post, $"/api/v1/orders/{order!.OrderId}/payments/sandbox/confirm", cart)).EnsureSuccessStatusCode();

        var staffEmail = await UseStaffToken();
        try
        {
            foreach (var status in new[] { "accepted", "assembling", "readyForDelivery" })
            {
                (await _client.PutAsJsonAsync($"/api/v1/admin/orders/{order.OrderId}/status", new { status })).EnsureSuccessStatusCode();
            }

            var withoutCourier = await _client.PutAsJsonAsync($"/api/v1/admin/orders/{order.OrderId}/status", new { status = "outForDelivery" });
            withoutCourier.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ErrorOf(withoutCourier)).Should().Be("CourierRequired");

            (await _client.PutAsJsonAsync($"/api/v1/admin/orders/{order.OrderId}/courier", new { courierName = "Courier", courierPhone = "123" }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await _client.PutAsJsonAsync($"/api/v1/admin/orders/{order.OrderId}/courier", new { courierName = "Courier", courierPhone = "+380671112233" }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            (await _client.PutAsJsonAsync($"/api/v1/admin/orders/{order.OrderId}/status", new { status = "outForDelivery" }))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            var card = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/admin/orders/{order.OrderId}");
            card.GetProperty("delivery").GetProperty("courierName").GetString().Should().Be("Courier");
            var history = card.GetProperty("statusHistory").EnumerateArray().ToList();
            history[^1].GetProperty("status").GetString().Should().Be("outForDelivery");
            history[^1].GetProperty("changedBy").GetString().Should().Be(staffEmail);
        }
        finally
        {
            _client.DefaultRequestHeaders.Authorization = null;
        }
    }

    [Fact]
    public async Task US_9_1_queue_hides_unpaid_orders()
    {
        var cart = await CreateCartWith(AcceptanceWebApplicationFactory.RedBouquetId, 1);
        var order = await (await Checkout(cart, DefaultSlot())).Content.ReadFromJsonAsync<CheckoutDocument>();

        await UseStaffToken();
        try
        {
            var queue = await _client.GetFromJsonAsync<JsonElement>("/api/v1/admin/orders");
            queue.EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).Should().NotContain(order!.OrderId);

            var unpaid = await _client.GetFromJsonAsync<JsonElement>("/api/v1/admin/orders?status=pendingPayment");
            unpaid.EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).Should().Contain(order.OrderId);
        }
        finally
        {
            _client.DefaultRequestHeaders.Authorization = null;
        }
    }

    [Fact]
    public async Task US_2_2_similar_bouquets_prefer_shared_colour()
    {
        var similar = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/catalog/products/{AcceptanceWebApplicationFactory.RedBouquetId}/similar?limit=1");

        similar.EnumerateArray().Single().GetProperty("id").GetGuid().Should().Be(AcceptanceWebApplicationFactory.SecondRedBouquetId);
    }

    private static DateTimeOffset KyivTime(DateOnly date, int hour, int minute)
    {
        var local = date.ToDateTime(new TimeOnly(hour, minute));
        return new DateTimeOffset(local, Kyiv.GetUtcOffset(local));
    }

    private static int CommerceHour(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, Kyiv).Hour;

    private DateTimeOffset DefaultSlot()
    {
        var kyivNow = TimeZoneInfo.ConvertTime(_factory.Time.GetUtcNow(), Kyiv);
        return KyivTime(DateOnly.FromDateTime(kyivNow.DateTime).AddDays(2), 12, 0);
    }

    private async Task<CartDocument> CreateCartWith(Guid productId, int quantity)
    {
        var cart = await (await _client.PostAsync("/api/v1/carts", null)).Content.ReadFromJsonAsync<CartDocument>();
        var add = await Send(HttpMethod.Post, $"/api/v1/carts/{cart!.Id}/items", cart, new { productId, quantity });
        add.StatusCode.Should().Be(HttpStatusCode.OK);
        return cart;
    }

    private async Task<CartDocument> GetCart(CartDocument cart) =>
        (await (await Send(HttpMethod.Get, $"/api/v1/carts/{cart.Id}", cart)).Content.ReadFromJsonAsync<CartDocument>())!;

    private Task<HttpResponseMessage> Checkout(
        CartDocument cart,
        DateTimeOffset scheduledFrom,
        string contactPhone = "+380501112233",
        string recipientPhone = "+380501112233",
        string city = "Chernivtsi",
        string? cardMessage = null) =>
        Send(HttpMethod.Post, "/api/v1/checkout", cart, new
        {
            cartId = cart.Id,
            contactName = "Test Customer",
            contactEmail = "customer@example.test",
            contactPhone,
            recipientName = "Test Recipient",
            recipientPhone,
            city,
            street = "Main Street",
            building = "1",
            scheduledFrom,
            scheduledTo = scheduledFrom.AddHours(2),
            cardMessage
        });

    private async Task<HttpResponseMessage> Send(HttpMethod method, string url, CartDocument cart, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Cart-Token", cart.SessionToken.ToString());
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private static async Task<string?> ErrorOf(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("error").GetString();
    }

    private Task<HttpResponseMessage> Login(string email, string password) =>
        _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });

    private async Task<string> UseStaffToken()
    {
        var email = $"staff-{Guid.NewGuid():N}@example.test";
        await AddStaffAsync(email, "CorrectHorseBattery1");
        var login = await Login(email, "CorrectHorseBattery1");
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return email;
    }

    private async Task AddStaffAsync(string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FioreDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
        var user = new AppUser { Id = Guid.NewGuid(), Email = email, FirstName = "Staff", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        user.PasswordHash = hasher.HashPassword(user, password);
        user.Roles.Add(new UserRole { UserId = user.Id, RoleId = 2, AssignedAt = DateTimeOffset.UtcNow });
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
    }

    private async Task<Guid> AddProduct(decimal price, int quantity = 10)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FioreDbContext>();
        var product = AcceptanceWebApplicationFactory.CreateProduct(Guid.NewGuid(), $"T-{Guid.NewGuid():N}"[..12], price, quantity);
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync();
        return product.Id;
    }

    private async Task ChangePrice(Guid productId, decimal price)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FioreDbContext>();
        var product = await dbContext.Products.SingleAsync(candidate => candidate.Id == productId);
        product.BasePrice = price;
        await dbContext.SaveChangesAsync();
    }

    private async Task<int> StockOf(Guid productId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FioreDbContext>();
        return (await dbContext.ProductInventory.SingleAsync(inventory => inventory.ProductId == productId)).QuantityAvailable;
    }

    private sealed record SlotsDocument(DateOnly Date, IReadOnlyList<SlotDocument> Slots);
    private sealed record SlotDocument(DateTimeOffset ScheduledFrom, bool IsAvailable);
    private sealed record CartDocument(Guid Id, Guid SessionToken, decimal DeliveryFeeAmount, decimal TotalAmount, decimal MinimumOrderAmount, bool MeetsMinimumOrder, bool HasPriceChanges);
    private sealed record CheckoutDocument(Guid OrderId, string OrderNumber, string TrackingCode);
}

public sealed class AdjustableTimeProvider : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public void Set(DateTimeOffset now) => _now = now.ToUniversalTime();
    public void Advance(TimeSpan by) => _now += by;
    public override DateTimeOffset GetUtcNow() => _now;
}

public sealed class AcceptanceWebApplicationFactory : WebApplicationFactory<Program>
{
    public static readonly Guid RedBouquetId = Guid.Parse("00000000-0000-0000-0000-00000000a001");
    public static readonly Guid SecondRedBouquetId = Guid.Parse("00000000-0000-0000-0000-00000000a002");
    public static readonly Guid SmallBouquetId = Guid.Parse("00000000-0000-0000-0000-00000000a003");
    public static readonly Guid BlueBouquetId = Guid.Parse("00000000-0000-0000-0000-00000000a004");
    private readonly string _databaseName = $"acceptance-tests-{Guid.NewGuid():N}";

    public AdjustableTimeProvider Time { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<FioreDbContext>>();
            services.AddDbContext<FioreDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            var expirationService = services.Single(descriptor => descriptor.ImplementationType == typeof(UnpaidOrderExpirationService));
            services.Remove(expirationService);

            using var scope = services.BuildServiceProvider().CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<FioreDbContext>();
            dbContext.Database.EnsureDeleted();
            dbContext.Database.EnsureCreated();

            var red = new CatalogFacet { Id = Guid.NewGuid(), Kind = ProductFacetKind.Color, Slug = "red", Name = "Red" };
            var blue = new CatalogFacet { Id = Guid.NewGuid(), Kind = ProductFacetKind.Color, Slug = "blue", Name = "Blue" };
            dbContext.CatalogFacets.AddRange(red, blue);
            dbContext.Products.AddRange(
                WithFacet(CreateProduct(RedBouquetId, "RED-1", 1000, 50), red),
                WithFacet(CreateProduct(SecondRedBouquetId, "RED-2", 1400, 50), red),
                CreateProduct(SmallBouquetId, "SMALL-1", 300, 50),
                WithFacet(CreateProduct(BlueBouquetId, "BLUE-1", 1600, 50), blue));
            dbContext.SaveChanges();
        });
    }

    public static Product CreateProduct(Guid id, string sku, decimal price, int quantity) => new()
    {
        Id = id,
        Sku = sku,
        Name = sku,
        BasePrice = price,
        Currency = "UAH",
        Status = ProductStatus.Available,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
        Inventory = new ProductInventory { ProductId = id, QuantityAvailable = quantity, ReorderLevel = 1, UpdatedAt = DateTimeOffset.UnixEpoch }
    };

    private static Product WithFacet(Product product, CatalogFacet facet)
    {
        product.Facets.Add(new ProductFacet { ProductId = product.Id, FacetId = facet.Id, Facet = facet });
        return product;
    }
}
