using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fiore.Api.Domain.Identity;
using Fiore.Api.Infrastructure.Persistence;
using Fiore.Api.Tests.Features.Catalog.ListProducts;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Fiore.Api.Tests.Features.Auth;

public sealed class AuthEndpointTests : IClassFixture<ListProductsWebApplicationFactory>
{
    private readonly ListProductsWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthEndpointTests(ListProductsWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Customer_can_register_then_access_own_profile()
    {
        var email = $"customer-{Guid.NewGuid():N}@example.test";
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "CorrectHorseBattery1",
            firstName = "Olena",
            lastName = "Flower"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var account = await response.Content.ReadFromJsonAsync<AuthResponse>();
        account.Should().NotBeNull();
        account!.Roles.Should().ContainSingle().Which.Should().Be("customer");
        account.AccessToken.Should().NotBeNullOrWhiteSpace();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        try
        {
            var profileResponse = await _client.GetAsync("/api/v1/auth/me");
            profileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var profile = await profileResponse.Content.ReadFromJsonAsync<ProfileResponse>();
            profile!.Email.Should().Be(email);
            profile.Roles.Should().Contain("customer");
        }
        finally
        {
            _client.DefaultRequestHeaders.Authorization = null;
        }
    }

    [Fact]
    public async Task Only_staff_can_manage_catalogue()
    {
        var unauthenticatedResponse = await _client.PostAsJsonAsync("/api/v1/admin/products", ValidProduct("NO-AUTH"));
        unauthenticatedResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var email = $"manager-{Guid.NewGuid():N}@example.test";
        await AddManagerAsync(email, "CorrectHorseBattery1");
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "CorrectHorseBattery1" });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var account = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account!.AccessToken);
        try
        {
            var createResponse = await _client.PostAsJsonAsync("/api/v1/admin/products", ValidProduct("MANAGER-001"));
            createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        }
        finally
        {
            _client.DefaultRequestHeaders.Authorization = null;
        }
    }

    private async Task AddManagerAsync(string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FioreDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
        var now = DateTimeOffset.UtcNow;
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            FirstName = "Manager",
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);
        user.Roles.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = 2,
            AssignedAt = now
        });
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
    }

    private static object ValidProduct(string sku) => new
    {
        sku,
        name = "Test bouquet",
        basePrice = 650m,
        quantityAvailable = 4,
        description = "A test bouquet",
        images = new[] { new { imageUrl = "https://example.test/bouquet.jpg", altText = "Bouquet" } }
    };

    private sealed record AuthResponse(Guid UserId, string Email, IReadOnlyList<string> Roles, string AccessToken);
    private sealed record ProfileResponse(string Email, IReadOnlyList<string> Roles);
}
