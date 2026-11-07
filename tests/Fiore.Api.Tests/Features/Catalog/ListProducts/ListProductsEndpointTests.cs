using System.Net;
using System.Net.Http.Json;
using Fiore.Api.Domain.Catalog;
using Fiore.Api.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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

    private sealed record CatalogResponse(IReadOnlyList<CatalogItem> Items, PaginationResponse Pagination);
    private sealed record CatalogItem(string Sku, bool IsAvailable);
    private sealed record PaginationResponse(int Page, int PageSize, int TotalItems, int TotalPages);
}

public sealed class ListProductsWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<FioreDbContext>>();
            services.AddDbContext<FioreDbContext>(options => options.UseInMemoryDatabase("catalog-endpoint-tests"));

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
