using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fiore.Api.Domain.Commerce;
using Fiore.Api.Domain.Identity;
using Fiore.Api.Infrastructure.Persistence;
using Fiore.Api.Tests.Features.Catalog.ListProducts;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Fiore.Api.Tests.Features.Admin;

public sealed class CourierAssignmentTests
{
    [Fact]
    public async Task Cannot_move_to_out_for_delivery_without_assigned_courier_then_succeeds_after_assignment()
    {
        using var factory = new ListProductsWebApplicationFactory();
        using var client = factory.CreateClient();
        var orderId = await SeedOrderAsync(factory, OrderStatus.Assembling);
        var token = await LoginStaffAsync(factory, client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var moveReadyResponse = await client.PutAsJsonAsync($"/api/v1/admin/orders/{orderId}/status", new { status = "readyForDelivery" });
        moveReadyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var missingCourierResponse = await client.PutAsJsonAsync($"/api/v1/admin/orders/{orderId}/status", new { status = "outForDelivery" });
        missingCourierResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var errorPayload = await missingCourierResponse.Content.ReadFromJsonAsync<ErrorResponse>();
        errorPayload!.Error.Should().Be("CourierRequired");

        var assignResponse = await client.PutAsJsonAsync($"/api/v1/admin/orders/{orderId}/courier", new
        {
            courierName = "Олексій Коваленко",
            courierPhone = "+380501234567"
        });
        assignResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var assigned = await assignResponse.Content.ReadFromJsonAsync<AssignCourierResponse>();
        assigned!.CourierName.Should().Be("Олексій Коваленко");
        assigned.CanMoveToOutForDelivery.Should().BeTrue();
        assigned.DeliverySummaryText.Should().Contain("Чернівці").And.Contain("Іванна Петренко").And.Contain("+380991112233");

        var summaryResponse = await client.GetAsync($"/api/v1/admin/orders/{orderId}/delivery-summary");
        summaryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await summaryResponse.Content.ReadFromJsonAsync<DeliverySummaryResponse>();
        summary!.Text.Should().Contain("Чернівці, Головна, 12, кв. 5").And.Contain("Іванна Петренко (+380991112233)");

        var outForDeliveryResponse = await client.PutAsJsonAsync($"/api/v1/admin/orders/{orderId}/status", new { status = "outForDelivery" });
        outForDeliveryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<Guid> SeedOrderAsync(ListProductsWebApplicationFactory factory, OrderStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FioreDbContext>();
        var now = DateTimeOffset.UtcNow;
        var orderId = Guid.NewGuid();
        var scheduledFrom = new DateTimeOffset(now.Date.AddDays(1).AddHours(10), TimeSpan.Zero);

        dbContext.Orders.Add(new Order
        {
            Id = orderId,
            SourceCartId = Guid.NewGuid(),
            TrackingCode = Fiore.Api.Features.Common.CommerceRules.NewTrackingCode(),
            ContactName = "Замовник",
            ContactEmail = "buyer@example.test",
            ContactPhone = "+380500000000",
            Status = status,
            Currency = "UAH",
            SubtotalAmount = 500m,
            DeliveryFeeAmount = 120m,
            TotalAmount = 620m,
            CreatedAt = now,
            UpdatedAt = now,
            Delivery = new OrderDelivery
            {
                OrderId = orderId,
                RecipientName = "Іванна Петренко",
                RecipientPhone = "+380991112233",
                City = "Чернівці",
                Street = "Головна",
                Building = "12",
                Apartment = "5",
                DeliveryNotes = "Подзвонити за 15 хв",
                ScheduledFrom = scheduledFrom,
                ScheduledTo = scheduledFrom.AddHours(2)
            },
            Payment = new Payment
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                Provider = "sandbox",
                ProviderPaymentId = $"pay-{orderId:N}",
                Status = PaymentStatus.Succeeded,
                Amount = 620m,
                Currency = "UAH",
                CreatedAt = now,
                UpdatedAt = now,
                CompletedAt = now
            }
        });
        await dbContext.SaveChangesAsync();
        return orderId;
    }

    private static async Task<string> LoginStaffAsync(ListProductsWebApplicationFactory factory, HttpClient client)
    {
        var email = $"staff-{Guid.NewGuid():N}@example.test";
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FioreDbContext>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
            var now = DateTimeOffset.UtcNow;
            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = email,
                FirstName = "Admin",
                CreatedAt = now,
                UpdatedAt = now
            };
            user.PasswordHash = passwordHasher.HashPassword(user, "CorrectHorseBattery1");
            user.Roles.Add(new UserRole { UserId = user.Id, RoleId = 1, AssignedAt = now });
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();
        }

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "CorrectHorseBattery1" });
        loginResponse.EnsureSuccessStatusCode();
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        return auth!.AccessToken;
    }

    private sealed record AuthResponse(string AccessToken);
    private sealed record ErrorResponse(string Error);
    private sealed record AssignCourierResponse(string CourierName, bool CanMoveToOutForDelivery, string DeliverySummaryText);
    private sealed record DeliverySummaryResponse(string Text);
}
