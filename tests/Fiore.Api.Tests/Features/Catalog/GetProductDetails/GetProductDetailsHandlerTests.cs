using Fiore.Api.Domain.Catalog;
using Fiore.Api.Features.Catalog.GetProductDetails;
using Fiore.Api.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Fiore.Api.Tests.Features.Catalog.GetProductDetails;

public sealed class GetProductDetailsHandlerTests
{
    [Fact]
    public async Task Handle_returns_a_visible_product_with_images_in_display_order()
    {
        await using var dbContext = CreateDbContext();
        var product = CreateProduct(ProductStatus.Available, 2);
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync();

        var handler = new GetProductDetailsHandler(dbContext);
        var response = await handler.Handle(new GetProductDetailsRequest(product.Id), CancellationToken.None);

        response.Should().NotBeNull();
        response!.Name.Should().Be("Spring bouquet");
        response.IsAvailable.Should().BeTrue();
        response.Images.Select(image => image.ImageUrl).Should().ContainInOrder(
            "https://cdn.example.test/primary.jpg",
            "https://cdn.example.test/secondary.jpg");
    }

    [Fact]
    public async Task Handle_hides_non_public_products()
    {
        await using var dbContext = CreateDbContext();
        var product = CreateProduct(ProductStatus.Hidden, 3);
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync();

        var handler = new GetProductDetailsHandler(dbContext);
        var response = await handler.Handle(new GetProductDetailsRequest(product.Id), CancellationToken.None);

        response.Should().BeNull();
    }

    private static FioreDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FioreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new FioreDbContext(options);
    }

    private static Product CreateProduct(ProductStatus status, int quantityAvailable)
    {
        var productId = Guid.NewGuid();

        return new Product
        {
            Id = productId,
            Sku = "FIO-SPRING-001",
            Name = "Spring bouquet",
            Description = "Seasonal flowers in a pastel palette.",
            BasePrice = 950,
            Currency = "UAH",
            Status = status,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
            Images =
            [
                new ProductImage
                {
                    Id = Guid.NewGuid(),
                    ProductId = productId,
                    ImageUrl = "https://cdn.example.test/secondary.jpg",
                    SortOrder = 1,
                    CreatedAt = DateTimeOffset.UnixEpoch
                },
                new ProductImage
                {
                    Id = Guid.NewGuid(),
                    ProductId = productId,
                    ImageUrl = "https://cdn.example.test/primary.jpg",
                    SortOrder = 0,
                    CreatedAt = DateTimeOffset.UnixEpoch
                }
            ],
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
