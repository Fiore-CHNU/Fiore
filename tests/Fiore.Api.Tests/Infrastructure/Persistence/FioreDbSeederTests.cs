using Fiore.Api.Domain.Identity;
using Fiore.Api.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Fiore.Api.Tests.Infrastructure.Persistence;

public sealed class FioreDbSeederTests
{
    [Fact]
    public async Task SeedAsync_populates_catalog_and_staff_idempotently()
    {
        var options = new DbContextOptionsBuilder<FioreDbContext>()
            .UseInMemoryDatabase($"fiore-seed-test-{Guid.NewGuid():N}")
            .Options;

        await using var dbContext = new FioreDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        var passwordHasher = new PasswordHasher<AppUser>();

        await FioreDbSeeder.SeedAsync(dbContext, passwordHasher);
        await FioreDbSeeder.SeedAsync(dbContext, passwordHasher);

        (await dbContext.Products.CountAsync()).Should().Be(6);
        (await dbContext.CatalogFacets.CountAsync()).Should().Be(12);
        (await dbContext.Users.CountAsync()).Should().Be(2);
    }
}
