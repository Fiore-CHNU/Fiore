using Fiore.Api.Domain.Catalog;
using Fiore.Api.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Fiore.Api.Features.Catalog.GetProductDetails;

public sealed record GetProductDetailsRequest(Guid ProductId) : IRequest<ProductDetailsResponse?>;

public sealed class GetProductDetailsHandler(FioreDbContext dbContext)
    : IRequestHandler<GetProductDetailsRequest, ProductDetailsResponse?>
{
    public Task<ProductDetailsResponse?> Handle(GetProductDetailsRequest request, CancellationToken cancellationToken)
    {
        return dbContext.Products
            .AsNoTracking()
            .Where(product => product.Id == request.ProductId && product.Status == ProductStatus.Available)
            .Select(product => new ProductDetailsResponse(
                product.Id,
                product.Sku,
                product.Name,
                product.Description,
                product.BasePrice,
                product.Currency,
                product.Inventory != null && product.Inventory.QuantityAvailable > 0,
                product.HeightCm,
                product.WidthCm,
                product.Images
                    .OrderBy(image => image.SortOrder)
                    .Select(image => new ProductImageResponse(
                        image.Id,
                        image.ImageUrl,
                        image.AltText,
                        image.SortOrder))
                    .ToList(),
                product.Composition.OrderBy(item => item.SortOrder)
                    .Select(item => new ProductCompositionResponse(item.FlowerName, item.StemCount, item.Note, item.SortOrder)).ToList(),
                product.Facets.OrderBy(link => link.Facet.Kind).ThenBy(link => link.Facet.Name)
                    .Select(link => new ProductFacetResponse(link.Facet.Id, link.Facet.Kind, link.Facet.Slug, link.Facet.Name)).ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }
}

public sealed record ProductDetailsResponse(
    Guid Id,
    string Sku,
    string Name,
    string? Description,
    decimal Price,
    string Currency,
    bool IsAvailable,
    decimal? HeightCm,
    decimal? WidthCm,
    IReadOnlyList<ProductImageResponse> Images,
    IReadOnlyList<ProductCompositionResponse> Composition,
    IReadOnlyList<ProductFacetResponse> Facets);

public sealed record ProductImageResponse(Guid Id, string ImageUrl, string? AltText, short SortOrder);
public sealed record ProductCompositionResponse(string FlowerName, short StemCount, string? Note, short SortOrder);
public sealed record ProductFacetResponse(Guid Id, ProductFacetKind Kind, string Slug, string Name);

public static class GetProductDetailsEndpoint
{
    public static IEndpointRouteBuilder MapCatalogDetailEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/catalog/products/{productId:guid}", async (
            Guid productId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var product = await sender.Send(new GetProductDetailsRequest(productId), cancellationToken);

            return product is null ? Results.NotFound() : Results.Ok(product);
        })
        .WithName("GetCatalogProductDetails")
        .WithOpenApi();

        return endpoints;
    }
}
