using Fiore.Api.Domain.Catalog;
using Fiore.Api.Features.Catalog.ListProducts;
using Fiore.Api.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Fiore.Api.Tests.Features.Catalog.ListProducts;

public sealed class ListProductsHandlerTests
{
    [Fact]
    public async Task Handle_returns_the_requested_page_and_catalogue_metadata()
    {
        await using var dbContext = CreateDbContext();
        var oldest = CreateProduct("FIO-001", "Old bouquet", 1, ProductStatus.Available, 4);
        var newest = CreateProduct("FIO-002", "New bouquet", 3, ProductStatus.Available, 0);
        var middle = CreateProduct("FIO-003", "Middle bouquet", 2, ProductStatus.Available, 2);
        var hidden = CreateProduct("FIO-004", "Hidden bouquet", 4, ProductStatus.Hidden, 6);

        dbContext.Products.AddRange(oldest, newest, middle, hidden);
        await dbContext.SaveChangesAsync();

        var handler = new ListProductsHandler(dbContext);
        var response = await handler.Handle(new ListProductsRequest(Page: 2, PageSize: 1), CancellationToken.None);

        response.Pagination.Should().Be(new PaginationMetadata(Page: 2, PageSize: 1, TotalItems: 3, TotalPages: 3));
        response.Items.Should().ContainSingle();
        response.Items[0].Sku.Should().Be("FIO-003");
        response.Items[0].ImageUrl.Should().Be("https://cdn.example.test/FIO-003.jpg");
        response.Items[0].IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void Validator_rejects_an_invalid_pagination_request()
    {
        var validator = new ListProductsRequestValidator();

        var validation = validator.Validate(new ListProductsRequest(Page: 0, PageSize: 101));

        validation.IsValid.Should().BeFalse();
        validation.Errors.Select(error => error.PropertyName).Should().Contain(["Page", "PageSize"]);
    }

    private static FioreDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FioreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new FioreDbContext(options);
    }

    private static Product CreateProduct(string sku, string name, int createdOrder, ProductStatus status, int quantityAvailable)
    {
        var productId = Guid.NewGuid();

        return new Product
        {
            Id = productId,
            Sku = sku,
            Name = name,
            BasePrice = 500,
            Currency = "UAH",
            Status = status,
            CreatedAt = DateTimeOffset.UnixEpoch.AddDays(createdOrder),
            UpdatedAt = DateTimeOffset.UnixEpoch.AddDays(createdOrder),
            Images =
            [
                new ProductImage
                {
                    Id = Guid.NewGuid(),
                    ProductId = productId,
                    ImageUrl = $"https://cdn.example.test/{sku}.jpg",
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
